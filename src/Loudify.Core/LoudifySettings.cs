namespace Loudify.Core;

public sealed record LoudifySettings
{
    public const double MinGainDb = 0.0;
    public const double MaxGainDb = 24.0;
    public const double MinDuckAmountDb = 3.0;
    public const double MaxDuckAmountDb = 20.0;
    public const double DefaultDuckAmountDb = 8.0;
    public const double MinVoiceMarginDb = 6.0;
    public const double MaxVoiceMarginDb = 30.0;
    public const double DefaultVoiceMarginDb = 12.0;

    public bool Enabled { get; init; } = true;

    /// <summary>Boost applied before the limiter, in dB.</summary>
    public double GainDb { get; init; } = 8.0;

    /// <summary>Highest sample peak the limiter lets through, in dBFS.</summary>
    public double CeilingDb { get; init; } = -1.0;

    public double ReleaseMs { get; init; } = 150.0;

    public string Hotkey { get; init; } = "Ctrl+Alt+L";

    public bool PlaySoundOnToggle { get; init; } = true;

    /// <summary>Lower the game while the user talks, so the headset's own sidetone stays audible.</summary>
    public bool DuckWhileTalking { get; init; }

    public double DuckAmountDb { get; init; } = DefaultDuckAmountDb;

    /// <summary>How far above the room's noise (dB) the microphone must go to count as talking.</summary>
    public double VoiceMarginDb { get; init; } = DefaultVoiceMarginDb;

    /// <summary>Microphone used to detect speech, or null for the Windows default communications device.</summary>
    public string? MicrophoneId { get; init; }

    /// <summary>Returns a copy with every value inside its supported range.</summary>
    public LoudifySettings Sanitized() => this with
    {
        GainDb = Math.Clamp(double.IsFinite(GainDb) ? GainDb : 0.0, MinGainDb, MaxGainDb),
        CeilingDb = Math.Clamp(double.IsFinite(CeilingDb) ? CeilingDb : -1.0, LimiterParameters.MinCeilingDb, 0.0),
        ReleaseMs = Math.Clamp(double.IsFinite(ReleaseMs) ? ReleaseMs : 150.0, LimiterParameters.MinReleaseMs, LimiterParameters.MaxReleaseMs),
        DuckAmountDb = Math.Clamp(double.IsFinite(DuckAmountDb) ? DuckAmountDb : DefaultDuckAmountDb, MinDuckAmountDb, MaxDuckAmountDb),
        VoiceMarginDb = Math.Clamp(double.IsFinite(VoiceMarginDb) ? VoiceMarginDb : DefaultVoiceMarginDb, MinVoiceMarginDb, MaxVoiceMarginDb),
        MicrophoneId = string.IsNullOrWhiteSpace(MicrophoneId) ? null : MicrophoneId,
        Hotkey = HotkeyDefinition.TryParse(Hotkey, out var hotkey) ? hotkey.ToString() : new LoudifySettings().Hotkey,
    };
}
