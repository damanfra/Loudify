namespace Loudify.Core;

/// <summary>
/// Measures the room's noise while the user stays quiet for a few seconds.
/// The result is a high percentile of the block levels rather than the mean,
/// so the occasional quieter moment does not set the threshold too low and the
/// fans' normal swings stay below it.
/// </summary>
public sealed class NoiseCalibration
{
    public const double DurationMs = 5000;
    public const double Percentile = 0.9;

    private readonly List<double> _levels = new();
    private double _elapsedMs;

    public double Progress => Math.Clamp(_elapsedMs / DurationMs, 0, 1);
    public bool Done => _elapsedMs >= DurationMs;

    public void Add(double levelDb, double blockMs)
    {
        if (Done)
            return;
        _levels.Add(Math.Max(levelDb, VoiceDucker.MinFloorDb));
        _elapsedMs += blockMs;
    }

    /// <summary>The measured noise level in dB, or null before any audio arrived.</summary>
    public double? Result()
    {
        if (_levels.Count == 0)
            return null;
        var sorted = _levels.OrderBy(l => l).ToList();
        int index = (int)Math.Ceiling(Percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
