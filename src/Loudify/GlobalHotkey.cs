using System.Runtime.InteropServices;
using Loudify.Core;

namespace Loudify;

/// <summary>System-wide hotkey; works while a game has focus.</summary>
internal sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 1;

    private bool _registered;

    public GlobalHotkey()
    {
        CreateHandle(new CreateParams());
    }

    public event EventHandler? Pressed;

    public static bool TryResolveKey(string keyName, out Keys key)
    {
        // "1" means the top-row digit key, which Keys calls D1.
        if (keyName.Length == 1 && char.IsDigit(keyName[0]))
            keyName = "D" + keyName;
        return Enum.TryParse(keyName, ignoreCase: true, out key) && !int.TryParse(keyName, out _)
            && (key & Keys.Modifiers) == 0;
    }

    /// <summary>Registers the hotkey, replacing the previous one. Returns false if
    /// the key is unknown or another program already owns the combination.</summary>
    public bool Register(HotkeyDefinition hotkey)
    {
        Unregister();
        if (!TryResolveKey(hotkey.Key, out Keys key))
            return false;
        // HotkeyModifiers uses the same bit values as the Win32 MOD_* flags.
        uint modifiers = (uint)hotkey.Modifiers | MOD_NOREPEAT;
        _registered = RegisterHotKey(Handle, HotkeyId, modifiers, (uint)key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
