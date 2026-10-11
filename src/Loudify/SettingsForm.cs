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
