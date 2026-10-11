namespace Loudify.Core;

/// <summary>
/// Turns microphone levels into the attenuation applied to the game while the
/// user talks. The threshold follows the room: it is the ambient noise floor
/// (fans, PC) plus a margin, so turning a fan on or off needs no adjustment.
/// Speech must stay above it briefly before ducking starts (so a key click does
/// not trigger it), ducking holds through short pauses between words, and then
/// fades back out.
/// </summary>
public sealed class VoiceDucker
{
    public const double MinSpeechMs = 40;
    public const double HoldMs = 500;
    public const double ReleaseMs = 400;

    /// <summary>The noise floor drops quickly to quieter levels, so pauses between
    /// words keep it at the real ambient level...</summary>
    public const double FloorFallMs = 300;
    /// <summary>...and rises slowly, so speech barely moves it while a new steady
    /// noise (a fan turned on) is learned within several seconds.</summary>
    public const double FloorRiseDbPerSecond = 1.5;
    public const double MinFloorDb = -100;

    private double _aboveMs;
    private double _sinceSpeechMs;

    public double MarginDb { get; set; } = LoudifySettings.DefaultVoiceMarginDb;
    public double DuckDb { get; set; } = LoudifySettings.DefaultDuckAmountDb;

    public bool Speaking { get; private set; }
    public double CurrentDuckDb { get; private set; }
    /// <summary>Estimated ambient noise level, or null before the first block.</summary>
    public double? NoiseFloorDb { get; private set; }
    /// <summary>Level above which the user counts as talking.</summary>
    public double ThresholdDb => (NoiseFloorDb ?? 0) + MarginDb;

    /// <summary>Feeds one block of microphone audio and returns the attenuation to apply now.</summary>
    public double Process(double levelDb, double blockMs)
    {
        TrackNoiseFloor(levelDb, blockMs);
        if (levelDb >= ThresholdDb)
        {
            _aboveMs += blockMs;
            if (_aboveMs >= MinSpeechMs)
            {
                Speaking = true;
                _sinceSpeechMs = 0;
            }
        }
        else
        {
            _aboveMs = 0;
            if (Speaking)
            {
                _sinceSpeechMs += blockMs;
                if (_sinceSpeechMs >= HoldMs)
                    Speaking = false;
            }
        }

        CurrentDuckDb = Speaking
            ? DuckDb
            : Math.Max(0.0, CurrentDuckDb - DuckDb * blockMs / ReleaseMs);
        return CurrentDuckDb;
    }

    private void TrackNoiseFloor(double levelDb, double blockMs)
    {
        levelDb = Math.Max(levelDb, MinFloorDb);
        if (NoiseFloorDb is not { } floor)
            NoiseFloorDb = levelDb;
        else if (levelDb < floor)
            NoiseFloorDb = floor + (levelDb - floor) * (1 - Math.Exp(-blockMs / FloorFallMs));
        else
            NoiseFloorDb = Math.Min(levelDb, floor + FloorRiseDbPerSecond * blockMs / 1000);
    }

    public void Reset()
    {
        NoiseFloorDb = null;
        _aboveMs = 0;
        _sinceSpeechMs = 0;
        Speaking = false;
        CurrentDuckDb = 0;
    }

    /// <summary>RMS level of interleaved samples in dBFS (-120 for silence).</summary>
    public static double LevelDb(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return -120.0;
        double sum = 0;
        foreach (float s in samples)
            sum += (double)s * s;
        double rms = Math.Sqrt(sum / samples.Length);
        return rms > 1e-6 ? Math.Max(20.0 * Math.Log10(rms), -120.0) : -120.0;
    }
}
