namespace Loudify.Core;

/// <summary>
/// Turns microphone levels into the attenuation applied to the game while the
/// user talks. Speech must stay above the threshold briefly before ducking
/// starts (so a key click does not trigger it), ducking holds through short
/// pauses between words, and then fades back out.
/// </summary>
public sealed class VoiceDucker
{
    public const double MinSpeechMs = 40;
    public const double HoldMs = 500;
    public const double ReleaseMs = 400;

    private double _aboveMs;
    private double _sinceSpeechMs;

    public double ThresholdDb { get; set; } = LoudifySettings.DefaultVoiceThresholdDb;
    public double DuckDb { get; set; } = LoudifySettings.DefaultDuckAmountDb;

    public bool Speaking { get; private set; }
    public double CurrentDuckDb { get; private set; }

    /// <summary>Feeds one block of microphone audio and returns the attenuation to apply now.</summary>
    public double Process(double levelDb, double blockMs)
    {
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

    public void Reset()
    {
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
