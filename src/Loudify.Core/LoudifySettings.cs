namespace Loudify.Core;

public sealed record LoudifySettings
{
    public const double MinGainDb = 0.0;
    public const double MaxGainDb = 24.0;

    public bool Enabled { get; init; } = true;

    /// <summary>Boost applied before the limiter, in dB.</summary>
    public double GainDb { get; init; } = 8.0;

    /// <summary>Highest sample peak the limiter lets through, in dBFS.</summary>
    public double CeilingDb { get; init; } = -1.0;

    public double ReleaseMs { get; init; } = 150.0;

    public string Hotkey { get; init; } = "Ctrl+Alt+L";

    public bool PlaySoundOnToggle { get; init; } = true;

    /// <summary>Returns a copy with every value inside its supported range.</summary>
    public LoudifySettings Sanitized() => this with
    {
        GainDb = Math.Clamp(double.IsFinite(GainDb) ? GainDb : 0.0, MinGainDb, MaxGainDb),
        CeilingDb = Math.Clamp(double.IsFinite(CeilingDb) ? CeilingDb : -1.0, LimiterParameters.MinCeilingDb, 0.0),
        ReleaseMs = Math.Clamp(double.IsFinite(ReleaseMs) ? ReleaseMs : 150.0, LimiterParameters.MinReleaseMs, LimiterParameters.MaxReleaseMs),
        Hotkey = HotkeyDefinition.TryParse(Hotkey, out var hotkey) ? hotkey.ToString() : new LoudifySettings().Hotkey,
    };
}
