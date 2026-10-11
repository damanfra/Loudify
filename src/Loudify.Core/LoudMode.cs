namespace Loudify.Core;

/// <summary>
/// Above about +18 dB most of the signal already sits at the limiter ceiling, so
/// more gain mostly adds compression. Loud mode raises perceived loudness instead:
/// a presence boost where the ear is most sensitive, less low end (which uses up
/// headroom), and a ceiling closer to 0 dBFS. It fades in between StartGainDb and
/// FullGainDb so moving the slider never changes the tone abruptly.
/// </summary>
public static class LoudMode
{
    public const double StartGainDb = 18.0;
    public const double FullGainDb = 24.0;

    public const double PresenceHz = 3000.0;
    public const double PresenceQ = 1.0;
    public const double PresenceGainDb = 4.0;
    public const double LowShelfHz = 120.0;
    public const double LowShelfGainDb = -3.0;
    public const double CeilingDb = -0.1;

    /// <summary>How much of loud mode applies at this gain, from 0 to 1.</summary>
    public static double Amount(double gainDb) =>
        Math.Clamp((gainDb - StartGainDb) / (FullGainDb - StartGainDb), 0.0, 1.0);

    /// <summary>The user's ceiling, moved towards loud mode's as the amount grows.</summary>
    public static double EffectiveCeilingDb(double ceilingDb, double amount) =>
        ceilingDb + (Math.Max(ceilingDb, CeilingDb) - ceilingDb) * amount;
}
