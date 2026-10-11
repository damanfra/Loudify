using Loudify.Core;

namespace Loudify.Core.Tests;

public class EqualizerApoConfigTests
{
    private const string LimiterPath = @"C:\Program Files\EqualizerAPO\config\Loudify\LoudifyLimiter-0123abcd.dll";

    private const string ShippedConfig =
        "Preamp: -6 dB\r\nInclude: example.txt\r\nGraphicEQ: 25 0; 40 0; 63 0; 100 0; 160 0; 250 0; 400 0; 630 0; 1000 0; 1600 0; 2500 0; 4000 0; 6300 0; 10000 0; 16000 0";

    [Fact]
    public void EnabledConfigLoadsLimiterWithNormalizedParameters()
    {
        var settings = new LoudifySettings { GainDb = 12, CeilingDb = -1, ReleaseMs = 150 };

        string config = EqualizerApoConfig.BuildLoudifyConfig(settings, LimiterPath);

        Assert.Contains("Stage: post-mix\r\n", config);
        Assert.Contains(
            "VSTPlugin: Library \"" + LimiterPath + "\" Gain 0.333333 Ceiling 0.833333 Release 0.141414\r\n",
            config);
    }

    [Fact]
    public void DisabledConfigHasNoFilters()
    {
        string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { Enabled = false }, LimiterPath);

        var commands = config.Split("\r\n").Where(l => l.Length > 0 && !l.StartsWith('#'));
        Assert.Empty(commands);
    }

    [Fact]
    public void GainIsCappedAtTheSafeMaximum()
    {
        string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { GainDb = 40 }, LimiterPath);

        Assert.Contains(" Gain 0.666667 ", config); // 24 dB of the plugin's 36 dB range
    }

    [Fact]
    public void LoudModeIsOffUpTo18Db()
    {
        string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { GainDb = 18 }, LimiterPath);

        Assert.DoesNotContain("Filter:", config);
        Assert.Contains(" Ceiling 0.833333 ", config); // -1 dB
    }

    [Fact]
    public void LoudModeFadesInAbove18Db()
    {
        string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { GainDb = 21 }, LimiterPath);

        Assert.Contains("Filter: ON LS Fc 120 Hz Gain -1.5 dB\r\n", config);
        Assert.Contains("Filter: ON PK Fc 3000 Hz Gain 2 dB Q 1\r\n", config);
        Assert.Contains(" Ceiling 0.908333 ", config); // -0.55 dB
        Assert.True(config.IndexOf("Filter:", StringComparison.Ordinal) < config.IndexOf("VSTPlugin:", StringComparison.Ordinal));
    }

    [Fact]
    public void LoudModeIsFullAt24Db()
    {
        string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { GainDb = 24 }, LimiterPath);

        Assert.Contains("Filter: ON LS Fc 120 Hz Gain -3 dB\r\n", config);
        Assert.Contains("Filter: ON PK Fc 3000 Hz Gain 4 dB Q 1\r\n", config);
        Assert.Contains(" Ceiling 0.983333 ", config); // -0.1 dB
    }

    [Fact]
    public void LoudModeKeepsAHigherUserCeiling()
    {
        Assert.Equal(0.0, LoudMode.EffectiveCeilingDb(0.0, 1.0));
        Assert.Equal(-1.0, LoudMode.EffectiveCeilingDb(-1.0, 0.0));
    }

    [Fact]
    public void ParameterValuesUseInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { GainDb = 6 }, LimiterPath);
            Assert.Contains(" Gain 0.166667 ", config);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ShippedDefaultConfigIsReplaced()
    {
        Assert.True(EqualizerApoConfig.IsDefaultConfig(ShippedConfig));

        string? result = EqualizerApoConfig.WithInclude(ShippedConfig);

        Assert.NotNull(result);
        Assert.DoesNotContain("Preamp", result);
        Assert.Contains("Include: loudify.txt\r\n", result);
    }

    [Fact]
    public void CustomConfigIsKeptAndIncludeAppended()
    {
        const string custom = "Preamp: -2 dB\r\nFilter: ON PK Fc 100 Hz Gain 3 dB Q 1";

        string? result = EqualizerApoConfig.WithInclude(custom);

        Assert.Equal(custom + "\r\nInclude: loudify.txt\r\n", result);
    }

    [Fact]
    public void ExistingIncludeIsLeftAlone()
    {
        Assert.Null(EqualizerApoConfig.WithInclude("Preamp: -2 dB\ninclude:   LOUDIFY.TXT \n"));
    }

    [Fact]
    public void MissingOrEmptyConfigGetsInclude()
    {
        Assert.Contains("Include: loudify.txt", EqualizerApoConfig.WithInclude(null));
        Assert.Contains("Include: loudify.txt", EqualizerApoConfig.WithInclude("  \r\n"));
    }
}
