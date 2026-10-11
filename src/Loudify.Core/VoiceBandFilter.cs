namespace Loudify.Core;

/// <summary>
/// Keeps roughly the speech band (300–3400 Hz) of the microphone signal used for
/// voice detection: fans, hum and rumble sit mostly below it, hiss mostly above.
/// Only the detector sees this; the microphone audio itself is untouched.
/// </summary>
public sealed class VoiceBandFilter
{
    public const double LowHz = 300;
    public const double HighHz = 3400;

    private readonly Biquad _highPass;
    private readonly Biquad _lowPass;

    public VoiceBandFilter(double sampleRate)
    {
        _highPass = Biquad.HighPass(sampleRate, LowHz);
        _lowPass = Biquad.LowPass(sampleRate, Math.Min(HighHz, sampleRate * 0.45));
    }

    public void Process(Span<float> samples)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)_lowPass.Process(_highPass.Process(samples[i]));
    }

    /// <summary>Second-order Butterworth section (RBJ cookbook), direct form I.</summary>
    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        public static Biquad HighPass(double sampleRate, double hz)
        {
            var (cos, alpha) = Prepare(sampleRate, hz);
            return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public static Biquad LowPass(double sampleRate, double hz)
        {
            var (cos, alpha) = Prepare(sampleRate, hz);
            return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        private static (double Cos, double Alpha) Prepare(double sampleRate, double hz)
        {
            double w = 2 * Math.PI * hz / sampleRate;
            return (Math.Cos(w), Math.Sin(w) / (2 * Math.Sqrt(0.5)));
        }

        public double Process(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
