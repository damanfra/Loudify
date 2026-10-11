using System.Globalization;

namespace Loudify.Core;

/// <summary>
/// Converts limiter settings to the normalized 0..1 VST parameter values.
/// Must stay in sync with native/LoudifyLimiter/src/Parameters.h.
/// </summary>
public static class LimiterParameters
{
    public const double MaxGainDb = 36.0;
    public const double MinCeilingDb = -6.0;
    public const double MinReleaseMs = 10.0;
    public const double MaxReleaseMs = 1000.0;

    public static double GainToNormalized(double db) => Clamp01(db / MaxGainDb);
    public static double CeilingToNormalized(double db) => Clamp01(1.0 - db / MinCeilingDb);
    public static double ReleaseToNormalized(double ms) => Clamp01((ms - MinReleaseMs) / (MaxReleaseMs - MinReleaseMs));

    public static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}
