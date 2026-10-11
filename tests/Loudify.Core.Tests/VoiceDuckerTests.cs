using Loudify.Core;

namespace Loudify.Core.Tests;

public class VoiceDuckerTests
{
    private const double Block = 10;

    private static VoiceDucker Create() => new() { ThresholdDb = -45, DuckDb = 8 };

    private static double Feed(VoiceDucker d, double levelDb, double ms)
    {
        double duck = 0;
        for (double t = 0; t < ms; t += Block)
            duck = d.Process(levelDb, Block);
        return duck;
    }

    [Fact]
    public void SilenceNeverDucks()
    {
        var d = Create();
        Assert.Equal(0, Feed(d, -80, 2000));
        Assert.False(d.Speaking);
    }

    [Fact]
    public void ShortClicksDoNotDuck()
    {
        var d = Create();
        for (int i = 0; i < 20; i++)
        {
            Feed(d, -20, 30); // shorter than MinSpeechMs
            Feed(d, -80, 100);
        }
        Assert.Equal(0, d.CurrentDuckDb);
    }

    [Fact]
    public void SpeechDucksQuicklyAndFullyAfterTheMinimumDuration()
    {
        var d = Create();
        Assert.Equal(0, Feed(d, -30, 30));
        Assert.Equal(8, Feed(d, -30, 20));
        Assert.True(d.Speaking);
    }

    [Fact]
    public void PausesBetweenWordsKeepTheDuck()
    {
        var d = Create();
        Feed(d, -30, 200);
        Assert.Equal(8, Feed(d, -80, 300)); // shorter than HoldMs
        Assert.Equal(8, Feed(d, -30, 100));
    }

    [Fact]
    public void DuckFadesOutAfterSpeechEnds()
    {
        var d = Create();
        Feed(d, -30, 200);
        Feed(d, -80, VoiceDucker.HoldMs);
        double midway = Feed(d, -80, VoiceDucker.ReleaseMs / 2);
        Assert.InRange(midway, 3, 5);
        Assert.Equal(0, Feed(d, -80, VoiceDucker.ReleaseMs));
        Assert.False(d.Speaking);
    }

    [Fact]
    public void LevelOfFullScaleSquareWaveIsZeroDb()
    {
        float[] square = Enumerable.Range(0, 480).Select(i => i % 2 == 0 ? 1f : -1f).ToArray();
        Assert.Equal(0, VoiceDucker.LevelDb(square), 6);
        Assert.Equal(-120, VoiceDucker.LevelDb(new float[480]));
        Assert.Equal(-120, VoiceDucker.LevelDb(ReadOnlySpan<float>.Empty));
        Assert.Equal(-20, VoiceDucker.LevelDb(Enumerable.Repeat(0.1f, 100).ToArray()), 3);
    }

    [Fact]
    public void DuckSettingsAreSanitized()
    {
        var s = new LoudifySettings { DuckAmountDb = 99, VoiceThresholdDb = double.NaN }.Sanitized();
        Assert.Equal(LoudifySettings.MaxDuckAmountDb, s.DuckAmountDb);
        Assert.Equal(LoudifySettings.DefaultVoiceThresholdDb, s.VoiceThresholdDb);
    }
}
