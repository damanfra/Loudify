namespace Loudify.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global hotkey written as text, e.g. "Ctrl+Alt+L". The key name is a
/// System.Windows.Forms.Keys name; resolving it is left to the Windows app.
/// </summary>
public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? text, out HotkeyDefinition hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (string rawPart in text.Split('+'))
        {
            string part = rawPart.Trim();
            HotkeyModifiers? modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => null,
            };
            if (modifier is { } m)
                modifiers |= m;
            else if (key == null && part.Length > 0)
                key = part;
            else
                return false;
        }

        // A bare key would fire while typing in games and chats.
        if (key == null || modifiers == HotkeyModifiers.None)
            return false;
        hotkey = new HotkeyDefinition(modifiers, key.Length == 1 ? key.ToUpperInvariant() : key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}
