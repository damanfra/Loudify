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

    private LoudifySettings _settings;
    private EqualizerApoInstallation? _apo;
    private string? _limiterPath;
    private string? _error;
    private bool _hotkeyRegistered;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settings = _store.Load();

        _enabledItem = new ToolStripMenuItem("Ligado", null, (_, _) => Toggle()) { CheckOnClick = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add("Configurações...", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSettings();
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

        if (_apo == null)
            ShowMissingEqualizerApo();
    }

    public LoudifySettings Settings => _settings;
    public string? Error => _error;
    public bool HotkeyRegistered => _hotkeyRegistered;

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
        _settings = settings;
        try
        {
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings still apply for this session.
        }

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
            RefreshUi();
        }
    }

    public void Reconnect()
    {
        Connect();
        Apply();
    }

    protected override void ExitThreadCore()
    {
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
        RefreshUi();
    }

    private void RegisterHotkey()
    {
        _hotkeyRegistered = HotkeyDefinition.TryParse(_settings.Hotkey, out var hotkey) && _hotkey.Register(hotkey);
    }

    private void RefreshUi()
    {
        _enabledItem.Checked = _settings.Enabled;
        _tray.Icon = _error != null ? _feedback.ErrorIcon : _settings.Enabled ? _feedback.OnIcon : _feedback.OffIcon;
        string state = _error != null ? "erro"
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EqualizerApoInstallation.DownloadUrl) { UseShellExecute = true });
    }
}
