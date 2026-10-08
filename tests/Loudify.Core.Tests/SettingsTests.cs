using Loudify.Core;

namespace Loudify.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "loudify-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = new LoudifySettings { Enabled = false, GainDb = 11, Hotkey = "Ctrl+Shift+F9", PlaySoundOnToggle = false };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var store = new SettingsStore(Path.Combine(_dir, "missing.json"));

        Assert.Equal(new LoudifySettings(), store.Load());
    }

    [Fact]
    public void CorruptFileGivesDefaults()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(new LoudifySettings(), new SettingsStore(path).Load());
    }

    [Fact]
    public void OutOfRangeValuesAreSanitized()
    {
        var settings = new LoudifySettings { GainDb = 99, CeilingDb = 3, ReleaseMs = double.NaN, Hotkey = "L" }.Sanitized();

        Assert.Equal(LoudifySettings.MaxGainDb, settings.GainDb);
        Assert.Equal(0, settings.CeilingDb);
        Assert.Equal(150, settings.ReleaseMs);
        Assert.Equal("Ctrl+Alt+L", settings.Hotkey);
    }

    [Theory]
    [InlineData("Ctrl+Alt+L", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "L")]
    [InlineData(" shift + ctrl + f9 ", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "f9")]
    [InlineData("Win+Alt+b", HotkeyModifiers.Win | HotkeyModifiers.Alt, "B")]
    public void HotkeysParse(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyDefinition.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("L")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    public void InvalidHotkeysAreRejected(string text)
    {
        Assert.False(HotkeyDefinition.TryParse(text, out _));
    }

    [Fact]
    public void HotkeyFormatsCanonically()
    {
        Assert.True(HotkeyDefinition.TryParse("alt+ctrl+l", out var hotkey));
        Assert.Equal("Ctrl+Alt+L", hotkey.ToString());
    }
}
