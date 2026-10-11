using Loudify.Core;

namespace Loudify.Core.Tests;

public class VoiceDuckerTests
{
    private const double Block = 10;

    /// <summary>A ducker that has learned a quiet room at -60 dB (threshold -48 dB).</summary>
    private static VoiceDucker Create()
    {
        var d = new VoiceDucker { MarginDb = 12, DuckDb = 8 };
        Feed(d, -60, 2000);
        return d;
    }

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
    public void ThresholdIsTheNoiseFloorPlusTheMargin()
    {
        var d = Create();
        Assert.Equal(-60, d.NoiseFloorDb!.Value, 1);
        Assert.Equal(-48, d.ThresholdDb, 1);
        Assert.False(d.Speaking);
    }

    [Fact]
    public void ANewFanIsLearnedAndStopsDucking()
    {
        var d = Create();
        Feed(d, -45, 1000); // louder than the old threshold: counts as talking at first
        Assert.True(d.Speaking);

        Feed(d, -45, 15000);
        Assert.False(d.Speaking);
        Assert.Equal(0, d.CurrentDuckDb);
        Assert.Equal(-45, d.NoiseFloorDb!.Value, 1);
        Assert.Equal(8, Feed(d, -20, 100)); // still reacts to the voice above the fan
    }

    [Fact]
    public void TurningAFanOffIsLearnedQuickly()
    {
        var d = new VoiceDucker { MarginDb = 12, DuckDb = 8 };
        Feed(d, -40, 2000);
        Feed(d, -65, 1500);
        Assert.InRange(d.NoiseFloorDb!.Value, -65.5, -64.5);
    }

    [Fact]
    public void SpeechBarelyMovesTheNoiseFloor()
    {
        var d = Create();
        for (int i = 0; i < 20; i++)
        {
            Feed(d, -30, 300); // words
            Feed(d, -60, 200); // pauses
        }
        Assert.InRange(d.NoiseFloorDb!.Value, -61, -58);
        Assert.True(d.Speaking);
    }

    [Fact]
    public void LongTalkWithShortPausesKeepsTheThreshold()
    {
        var d = Create();
        // 30 s of talking; the short pauses never drop back to the fan level
        // (breath, room echo), so they stay below the threshold but above the floor.
        for (int i = 0; i < 60; i++)
        {
            Feed(d, -30, 400);
            Feed(d, -52, 100);
        }
        Assert.InRange(d.NoiseFloorDb!.Value, -61, -56);
        Assert.Equal(8, d.CurrentDuckDb);
        Assert.Equal(8, Feed(d, -42, 100)); // a quieter syllable still counts
    }

    [Theory]
    [InlineData(1000, -1, 1)]
    [InlineData(100, -100, -15)]
    [InlineData(10000, -100, -15)]
    public void VoiceBandFilterKeepsSpeechFrequencies(double hz, double minDb, double maxDb)
    {
        const int rate = 48000;
        var filter = new VoiceBandFilter(rate);
        float[] input = Enumerable.Range(0, rate).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();
        float[] output = (float[])input.Clone();
        filter.Process(output);

        // Skip the first 100 ms while the filter settles.
        double gainDb = VoiceDucker.LevelDb(output.AsSpan(4800)) - VoiceDucker.LevelDb(input.AsSpan(4800));
        Assert.InRange(gainDb, minDb, maxDb);
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
        var s = new LoudifySettings { DuckAmountDb = 99, VoiceMarginDb = double.NaN }.Sanitized();
        Assert.Equal(LoudifySettings.MaxDuckAmountDb, s.DuckAmountDb);
        Assert.Equal(LoudifySettings.DefaultVoiceMarginDb, s.VoiceMarginDb);
    }
}
