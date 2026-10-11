using Loudify.Core;

namespace Loudify;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Loudify", "settings.json"));

    private readonly Feedback _feedback = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly GlobalHotkey _hotkey = new();
    private readonly System.Windows.Forms.Timer _applyDebounce = new() { Interval = 150 };
    // One update check per start, a few seconds in so it never delays startup.
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 5_000 };
    private readonly ToolStripMenuItem _updateItem;

    private LoudifySettings _settings;
    private EqualizerApoInstallation? _apo;
    private string? _limiterPath;
    private string? _error;
    private bool _hotkeyRegistered;
    private SettingsForm? _settingsForm;
    private ReleaseInfo? _availableUpdate;
    private MicDucking? _ducking;
    private bool _updateBusy;

    public TrayApplicationContext(bool updated = false)
    {
        _settings = _store.Load();

        _enabledItem = new ToolStripMenuItem("Ligado", null, (_, _) => Toggle()) { CheckOnClick = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add("Configurações...", null, (_, _) => ShowSettings());
        _updateItem = new ToolStripMenuItem("Verificar atualizações...", null, (_, _) => UpdateOrCheck());
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSettings();
        };

        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_availableUpdate != null)
                InstallUpdate(_availableUpdate);
        };

        _hotkey.Pressed += (_, _) => Toggle();
        _applyDebounce.Tick += (_, _) =>
        {
            _applyDebounce.Stop();
            Apply();
        };

        RegisterHotkey();
        Connect();
        Apply();

        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Stop();
            CheckForUpdates(interactive: false);
        };
        _updateTimer.Start();

        if (updated)
            _tray.ShowBalloonTip(5000, "Loudify atualizado", $"Versão {UpdateCheck.Format(Updater.CurrentVersion)} instalada.", ToolTipIcon.Info);

        if (_apo == null)
            ShowMissingEqualizerApo();
    }

    public LoudifySettings Settings => _settings;
    public string? Error => _error;
    public bool HotkeyRegistered => _hotkeyRegistered;
    public bool UpdateBusy => _updateBusy;
    public ReleaseInfo? AvailableUpdate => _availableUpdate;
    /// <summary>Running only while the boost is on and "duck while talking" is enabled.</summary>
    public MicDucking? Ducking => _ducking;

    /// <summary>Installs the update found earlier, or checks for one.</summary>
    public void UpdateOrCheck()
    {
        if (_availableUpdate != null)
            InstallUpdate(_availableUpdate);
        else
            CheckForUpdates(interactive: true);
    }

    public event EventHandler? StateChanged;

    public void Toggle()
    {
        UpdateSettings(_settings with { Enabled = !_settings.Enabled }, immediate: true);
        if (_settings.PlaySoundOnToggle)
            _feedback.PlayToggle(_settings.Enabled);
    }

    /// <summary>Saves new settings and applies them; slider moves are debounced so
    /// Equalizer APO is not reloaded on every tick.</summary>
    public void UpdateSettings(LoudifySettings settings, bool immediate)
    {
        settings = settings.Sanitized();
        bool hotkeyChanged = settings.Hotkey != _settings.Hotkey;
        Save(settings);

        if (hotkeyChanged)
            RegisterHotkey();

        if (immediate)
        {
            _applyDebounce.Stop();
            Apply();
        }
        else
        {
            _applyDebounce.Stop();
            _applyDebounce.Start();
            UpdateDucking();
            RefreshUi();
        }
    }

    /// <summary>Looks for a newer release. Interactive checks always report the result;
    /// automatic ones stay silent unless an update is found.</summary>
    public async void CheckForUpdates(bool interactive)
    {
        if (_updateBusy)
            return;
        _updateBusy = true;
        RefreshUi();
        ReleaseInfo? latest;
        try
        {
            latest = await Updater.GetLatestReleaseAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _updateBusy = false;
            RefreshUi();
            if (interactive)
                MessageBox.Show("Não foi possível verificar atualizações:\n" + ex.Message, "Loudify", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _updateBusy = false;

        if (latest == null || !UpdateCheck.IsNewer(latest, Updater.CurrentVersion))
        {
            _availableUpdate = null;
            RefreshUi();
            if (interactive)
                MessageBox.Show($"Você já está na versão mais recente ({UpdateCheck.Format(Updater.CurrentVersion)}).",
                    "Loudify", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _availableUpdate = latest;
        RefreshUi();
        if (interactive)
        {
            var answer = MessageBox.Show(
                $"A versão {UpdateCheck.Format(latest.Version)} do Loudify está disponível (você usa a {UpdateCheck.Format(Updater.CurrentVersion)}).\n\n" +
                "Atualizar agora? O Loudify baixa a nova versão e reabre sozinho.",
                "Loudify", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
                InstallUpdate(latest);
        }
        else
        {
            _tray.ShowBalloonTip(10000, "Atualização do Loudify disponível",
                $"Versão {UpdateCheck.Format(latest.Version)} disponível. Clique aqui para atualizar.", ToolTipIcon.Info);
        }
    }

    /// <summary>Downloads and installs the release, then restarts into it.</summary>
    private async void InstallUpdate(ReleaseInfo release)
    {
        if (_updateBusy)
            return;
        _updateBusy = true;
        RefreshUi();
        try
        {
            string downloaded = await Updater.DownloadAsync(release, CancellationToken.None);
            Updater.InstallAndRestart(downloaded);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
            or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            _updateBusy = false;
            RefreshUi();
            var open = MessageBox.Show(
                "Não foi possível atualizar automaticamente:\n" + ex.Message + "\n\nAbrir a página da versão para baixar manualmente?",
                "Loudify", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (open == DialogResult.Yes)
                OpenUrl(release.PageUrl);
            return;
        }
        ExitThread();
    }

    private void Save(LoudifySettings settings)
    {
        _settings = settings.Sanitized();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings still apply for this session.
        }
    }

    public void Reconnect()
    {
        Connect();
        Apply();
    }

    protected override void ExitThreadCore()
    {
        _ducking?.Dispose();
        _ducking = null;

        // Equalizer APO keeps applying its config without Loudify running; turn the
        // boost off so closing the app restores normal volume. The saved setting is
        // kept, so the next start restores the previous state.
        if (_apo != null && _limiterPath != null)
        {
            try
            {
                _apo.WriteLoudifyConfig(EqualizerApoConfig.BuildLoudifyConfig(_settings with { Enabled = false }, _limiterPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        _settingsForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _hotkey.Dispose();
        _applyDebounce.Dispose();
        _updateTimer.Dispose();
        _feedback.Dispose();
        base.ExitThreadCore();
    }

    private void Connect()
    {
        _apo = EqualizerApoInstallation.Detect();
        _limiterPath = null;
        _error = null;
        if (_apo == null)
            return;
        try
        {
            _limiterPath = _apo.DeployLimiter();
            _apo.EnsureIncluded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _limiterPath = null;
            _error = "Não foi possível preparar o Equalizer APO: " + ex.Message;
        }
    }

    private void Apply()
    {
        if (_apo == null)
        {
            _error = "Equalizer APO não encontrado.";
        }
        else if (_limiterPath != null)
        {
            try
            {
                _apo.WriteLoudifyConfig(EqualizerApoConfig.BuildLoudifyConfig(_settings, _limiterPath));
                _error = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _error = "Não foi possível gravar a configuração: " + ex.Message;
            }
        }
        UpdateDucking();
        RefreshUi();
    }

    /// <summary>Starts, reconfigures or stops listening to the microphone. Ducking only
    /// has an effect while the limiter is loaded, i.e. while the boost is on.</summary>
    private void UpdateDucking()
    {
        bool active = _settings.Enabled && _settings.DuckWhileTalking && _apo != null && _limiterPath != null;
        if (_ducking != null && (!active || _ducking.DeviceId != _settings.MicrophoneId))
        {
            _ducking.Dispose();
            _ducking = null;
        }
        if (active && _ducking == null)
            _ducking = new MicDucking(_settings.MicrophoneId, _settings.VoiceMarginDb, _settings.DuckAmountDb, _settings.CalibratedNoiseDb);
        else if (active)
            _ducking!.Configure(_settings.VoiceMarginDb, _settings.DuckAmountDb, _settings.CalibratedNoiseDb);
        else if (_ducking != null)
        {
            _ducking.Dispose();
            _ducking = null;
        }
    }

    private void RegisterHotkey()
    {
        _hotkeyRegistered = HotkeyDefinition.TryParse(_settings.Hotkey, out var hotkey) && _hotkey.Register(hotkey);
    }

    private void RefreshUi()
    {
        _enabledItem.Checked = _settings.Enabled;
        _tray.Icon = _error != null ? _feedback.ErrorIcon : _settings.Enabled ? _feedback.OnIcon : _feedback.OffIcon;
        _updateItem.Text = _availableUpdate != null
            ? $"Atualizar para a versão {UpdateCheck.Format(_availableUpdate.Version)}"
            : "Verificar atualizações...";
        _updateItem.Enabled = !_updateBusy;
        string state = _updateBusy ? "atualizando..."
            : _error != null ? "erro"
            : _settings.Enabled ? $"ligado (+{_settings.GainDb:0} dB)"
            : "desligado";
        // NotifyIcon.Text is limited to 63 characters.
        string text = $"Loudify: {state} · {_settings.Hotkey}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
            _settingsForm = new SettingsForm(this);
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ShowMissingEqualizerApo()
    {
        var answer = MessageBox.Show(
            "O Loudify precisa do Equalizer APO (gratuito) para processar o áudio do Windows.\n\n" +
            "Na instalação, marque o seu headset na lista de dispositivos de reprodução e reinicie o Windows. " +
            "Depois abra o Loudify de novo.\n\nAbrir a página de download agora?",
            "Loudify", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
            OpenUrl(EqualizerApoInstallation.DownloadUrl);
    }

    private static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
