using Loudify.Core;

namespace Loudify;

/// <summary>Small settings window: on/off, gain, hotkey and startup. Changes apply live.</summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayApplicationContext _app;
    private readonly CheckBox _enabled = new() { Text = "Aumento de volume ligado", AutoSize = true };
    private readonly Label _gainLabel = new() { AutoSize = true };
    private readonly Label _loudWarning = new()
    {
        Text = $"Acima de +{LoudMode.StartGainDb:0} dB o som é comprimido e pode distorcer.",
        AutoSize = true,
        MaximumSize = new Size(340, 0),
        ForeColor = Color.Firebrick,
    };
    private readonly TrackBar _gain = new()
    {
        Minimum = (int)LoudifySettings.MinGainDb,
        Maximum = (int)LoudifySettings.MaxGainDb,
        TickFrequency = 3,
        LargeChange = 3,
        Width = 320,
    };
    private readonly CheckBox _duck = new() { Text = "Abaixar o jogo enquanto eu falo", AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
    private readonly FlowLayoutPanel _duckPanel = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        Margin = new Padding(18, 0, 0, 6),
    };
    private readonly Label _duckLabel = new() { AutoSize = true };
    private readonly TrackBar _duckAmount = new()
    {
        Minimum = (int)LoudifySettings.MinDuckAmountDb,
        Maximum = (int)LoudifySettings.MaxDuckAmountDb,
        TickFrequency = 1,
        LargeChange = 2,
        Width = 300,
    };
    private readonly ComboBox _mic = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly Label _micLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0) };
    private readonly ProgressBar _micMeter = new() { Minimum = 0, Maximum = 100, Width = 300, Height = 10 };
    private readonly Button _calibrate = new() { Text = "Calibrar ruído do ambiente", AutoSize = true };
    private readonly Button _autoNoise = new() { Text = "Automático", AutoSize = true };
    private readonly Label _thresholdLabel = new() { AutoSize = true };
    private readonly TrackBar _threshold = new()
    {
        Minimum = (int)LoudifySettings.MinVoiceMarginDb,
        Maximum = (int)LoudifySettings.MaxVoiceMarginDb,
        TickFrequency = 2,
        LargeChange = 2,
        Width = 300,
    };
    private readonly System.Windows.Forms.Timer _meterTimer = new() { Interval = 80 };
    private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 180, ShortcutsEnabled = false };
    private readonly Label _hotkeyStatus = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly CheckBox _sound = new() { Text = "Tocar um som ao ligar/desligar", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Iniciar com o Windows", AutoSize = true };
    private readonly Button _checkUpdate = new() { Text = "Verificar atualizações", AutoSize = true };
    private readonly Label _version = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 8, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(340, 0) };
    private readonly Button _retry = new() { Text = "Verificar novamente", AutoSize = true };
    private bool _updating;

    public SettingsForm(TrayApplicationContext app)
    {
        _app = app;
        Text = "Loudify v" + UpdateCheck.Format(Updater.CurrentVersion);
        Icon = AppIcon.Load();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        hotkeyRow.Controls.Add(new Label { Text = "Atalho:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
        hotkeyRow.Controls.Add(_hotkey);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(_enabled);
        layout.Controls.Add(_gainLabel);
        layout.Controls.Add(_gain);
        layout.Controls.Add(_loudWarning);
        layout.Controls.Add(_duck);
        _duckPanel.Controls.Add(new Label
        {
            Text = "Deixa o retorno de voz do próprio headset (sidetone) audível sobre o jogo amplificado.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
        });
        _duckPanel.Controls.Add(new Label { Text = "Microfone:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        _duckPanel.Controls.Add(_mic);
        _duckPanel.Controls.Add(_duckLabel);
        _duckPanel.Controls.Add(_duckAmount);
        _duckPanel.Controls.Add(_micLabel);
        _duckPanel.Controls.Add(_micMeter);
        var calibrateRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        calibrateRow.Controls.Add(_calibrate);
        calibrateRow.Controls.Add(_autoNoise);
        _duckPanel.Controls.Add(calibrateRow);
        _duckPanel.Controls.Add(new Label
        {
            Text = "Clique em Calibrar e fique 5 segundos em silêncio, com os ventiladores como de costume. O valor medido fica salvo e fixo.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
        });
        _duckPanel.Controls.Add(_thresholdLabel);
        _duckPanel.Controls.Add(_threshold);
        _duckPanel.Controls.Add(new Label
        {
            Text = "Aumente a margem se o jogo baixar sem você falar; diminua se não baixar quando você fala baixo.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
        });
        layout.Controls.Add(_duckPanel);
        layout.Controls.Add(hotkeyRow);
        layout.Controls.Add(new Label
        {
            Text = "Clique no campo e pressione a nova combinação (com Ctrl, Alt, Shift ou Win).",
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            ForeColor = SystemColors.GrayText,
        });
        layout.Controls.Add(_hotkeyStatus);
        layout.Controls.Add(_sound);
        layout.Controls.Add(_startup);
        var updateRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        updateRow.Controls.Add(_checkUpdate);
        updateRow.Controls.Add(_version);
        layout.Controls.Add(updateRow);
        layout.Controls.Add(_status);
        layout.Controls.Add(_retry);
        layout.Controls.Add(new Label
        {
            Text = "by Daniel Manfrini · Powered by Claude Code",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            Font = new Font(Font.FontFamily, Font.Size - 1),
            ForeColor = SystemColors.GrayText,
        });
        Controls.Add(layout);

        _enabled.CheckedChanged += (_, _) =>
        {
            if (!_updating && _enabled.Checked != _app.Settings.Enabled)
                _app.Toggle();
        };
        _gain.ValueChanged += (_, _) =>
        {
            if (!_updating)
                _app.UpdateSettings(_app.Settings with { GainDb = _gain.Value }, immediate: false);
        };
        _duck.CheckedChanged += (_, _) =>
        {
            if (!_updating)
                _app.UpdateSettings(_app.Settings with { DuckWhileTalking = _duck.Checked }, immediate: true);
        };
        _calibrate.Click += (_, _) => _app.Ducking?.StartCalibration();
        _autoNoise.Click += (_, _) => _app.UpdateSettings(_app.Settings with { CalibratedNoiseDb = null }, immediate: true);
        _mic.DropDown += (_, _) => FillMicrophones();
        _mic.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _mic.SelectedItem is MicItem item)
                // A calibration belongs to one microphone: drop it when switching.
                _app.UpdateSettings(_app.Settings with { MicrophoneId = item.Id, CalibratedNoiseDb = null }, immediate: true);
        };
        _duckAmount.ValueChanged += (_, _) =>
        {
            if (!_updating)
                _app.UpdateSettings(_app.Settings with { DuckAmountDb = _duckAmount.Value }, immediate: false);
        };
        _threshold.ValueChanged += (_, _) =>
        {
            if (!_updating)
                _app.UpdateSettings(_app.Settings with { VoiceMarginDb = _threshold.Value }, immediate: false);
        };
        _meterTimer.Tick += (_, _) => UpdateMeter();
        _meterTimer.Start();
        FormClosed += (_, _) => _meterTimer.Dispose();
        _sound.CheckedChanged += (_, _) =>
        {
            if (!_updating)
                _app.UpdateSettings(_app.Settings with { PlaySoundOnToggle = _sound.Checked }, immediate: true);
        };
        _startup.CheckedChanged += (_, _) =>
        {
            if (!_updating)
                StartupRegistration.SetEnabled(_startup.Checked);
        };
        _checkUpdate.Click += (_, _) => _app.UpdateOrCheck();
        _hotkey.KeyDown += OnHotkeyKeyDown;
        _retry.Click += (_, _) => _app.Reconnect();

        _app.StateChanged += OnStateChanged;
        FormClosed += (_, _) => _app.StateChanged -= OnStateChanged;

        _startup.Checked = StartupRegistration.IsEnabled;
        UpdateControls();
    }

    private void OnStateChanged(object? sender, EventArgs e) => UpdateControls();

    private void UpdateControls()
    {
        _updating = true;
        try
        {
            var s = _app.Settings;
            _enabled.Checked = s.Enabled;
            _gain.Value = (int)Math.Round(s.GainDb);
            double loud = LoudMode.Amount(s.GainDb);
            _gainLabel.Text = loud > 0 ? $"Ganho: +{s.GainDb:0} dB · modo mais alto {loud:0%}" : $"Ganho: +{s.GainDb:0} dB";
            _gainLabel.ForeColor = loud > 0 ? Color.Firebrick : SystemColors.ControlText;
            _loudWarning.Visible = loud > 0;
            _hotkey.Text = s.Hotkey;
            _hotkeyStatus.Text = _app.HotkeyRegistered ? "" : "Atalho indisponível: outro programa já o usa. Escolha outro.";
            _hotkeyStatus.Visible = !_app.HotkeyRegistered;
            _sound.Checked = s.PlaySoundOnToggle;
            _duck.Checked = s.DuckWhileTalking;
            _duckPanel.Visible = s.DuckWhileTalking;
            if (s.DuckWhileTalking && _mic.Items.Count == 0)
                FillMicrophones();
            _duckAmount.Value = (int)Math.Round(s.DuckAmountDb);
            _duckLabel.Text = $"Abaixar o jogo em {s.DuckAmountDb:0} dB enquanto você fala";
            _threshold.Value = (int)Math.Round(s.VoiceMarginDb);
            _thresholdLabel.Text = $"Sensibilidade: voz {s.VoiceMarginDb:0} dB acima do ruído do ambiente";
            var update = _app.AvailableUpdate;
            _checkUpdate.Text = _app.UpdateBusy ? "Aguarde..." : update != null ? "Atualizar" : "Verificar atualizações";
            _version.Text = "Versão " + UpdateCheck.Format(Updater.CurrentVersion)
                + (update != null ? $" · nova: {UpdateCheck.Format(update.Version)}" : "");
            _version.ForeColor = update != null ? Color.SeaGreen : SystemColors.GrayText;
            _checkUpdate.Enabled = !_app.UpdateBusy;
            _status.Text = _app.Error ?? "Equalizer APO conectado. O limitador evita distorção mesmo com ganho alto.";
            _status.ForeColor = _app.Error != null ? Color.Firebrick : SystemColors.GrayText;
            _retry.Visible = _app.Error != null;
        }
        finally
        {
            _updating = false;
        }
    }

    private sealed record MicItem(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>Lists the default entry plus the connected microphones and selects the saved one.
    /// Refreshed when the list opens, so a headset plugged in later shows up.</summary>
    private void FillMicrophones()
    {
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            string? selected = _app.Settings.MicrophoneId;
            var items = new List<MicItem> { new(null, "Padrão de comunicação do Windows") };
            items.AddRange(MicDucking.ListMicrophones().Select(m => new MicItem(m.Id, m.Name)));
            if (selected != null && items.All(i => i.Id != selected))
                items.Add(new MicItem(selected, "Microfone escolhido (desconectado)"));

            _mic.BeginUpdate();
            _mic.Items.Clear();
            _mic.Items.AddRange(items.ToArray());
            _mic.SelectedIndex = items.FindIndex(i => i.Id == selected);
            _mic.EndUpdate();
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    /// <summary>Live microphone level, so the threshold can be set by just talking.</summary>
    private void UpdateMeter()
    {
        if (!_duckPanel.Visible)
            return;
        var ducking = _app.Ducking;
        if (ducking == null)
        {
            _micLabel.Text = _app.Settings.Enabled ? "Microfone: iniciando..." : "Ligue o aumento de volume para usar.";
            _micLabel.ForeColor = SystemColors.GrayText;
            _micMeter.Value = 0;
            return;
        }
        if (ducking.Error != null)
        {
            _micLabel.Text = ducking.Error;
            _micLabel.ForeColor = Color.Firebrick;
            _micMeter.Value = 0;
            return;
        }
        double level = ducking.LevelDb;
        const double range = 80; // meter shows -80..0 dB
        _micMeter.Value = (int)Math.Clamp((level + range) / range * 100, 0, 100);

        if (ducking.TakeCalibrationResult() is { } measured)
            _app.UpdateSettings(_app.Settings with { CalibratedNoiseDb = Math.Round(measured, 1) }, immediate: true);
        bool calibrated = _app.Settings.CalibratedNoiseDb != null;
        if (ducking.CalibrationProgress is { } progress)
        {
            _calibrate.Enabled = false;
            _calibrate.Text = $"Silêncio... {Math.Ceiling((1 - progress) * NoiseCalibration.DurationMs / 1000):0} s";
        }
        else
        {
            _calibrate.Enabled = true;
            _calibrate.Text = calibrated ? "Calibrar de novo" : "Calibrar ruído do ambiente";
        }
        _autoNoise.Visible = calibrated;

        string room = ducking.CalibrationProgress != null
            ? "\nCalibrando: fique em silêncio..."
            : ducking.NoiseFloorDb is { } floor
                ? $"\n{(calibrated ? "Ruído calibrado" : "Ruído do ambiente (automático)")}: {Math.Max(floor, -99):0} dB · voz a partir de {Math.Max(ducking.ThresholdDb, -99):0} dB"
                : "";
        _micLabel.Text = $"{ducking.MicrophoneName ?? "Microfone"}: {Math.Max(level, -99):0} dB" + (ducking.Speaking ? " · falando" : "") + room
            + (ducking.PluginConnected ? "" : "\nLimitador não conectado: toque algum som para o Equalizer APO carregá-lo.");
        _micLabel.ForeColor = !ducking.PluginConnected ? Color.Firebrick
            : ducking.Speaking ? Color.SeaGreen : SystemColors.ControlText;
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        Keys key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        var modifiers = HotkeyModifiers.None;
        if (e.Control) modifiers |= HotkeyModifiers.Ctrl;
        if (e.Alt) modifiers |= HotkeyModifiers.Alt;
        if (e.Shift) modifiers |= HotkeyModifiers.Shift;
        if (IsWinDown())
            modifiers |= HotkeyModifiers.Win;
        if (modifiers == HotkeyModifiers.None)
            return;

        string keyName = key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString();
        if (HotkeyDefinition.TryParse(new HotkeyDefinition(modifiers, keyName).ToString(), out var hotkey))
            _app.UpdateSettings(_app.Settings with { Hotkey = hotkey.ToString() }, immediate: true);
    }

    private static bool IsWinDown() =>
        (GetKeyState((int)Keys.LWin) & 0x8000) != 0 || (GetKeyState((int)Keys.RWin) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
