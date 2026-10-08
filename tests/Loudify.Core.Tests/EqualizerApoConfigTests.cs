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
            "VSTPlugin: Library \"" + LimiterPath + "\" Gain 0.5 Ceiling 0.833333 Release 0.141414\r\n",
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

        Assert.Contains(" Gain 0.75 ", config); // 18 dB of the plugin's 24 dB range
    }

    [Fact]
    public void ParameterValuesUseInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            string config = EqualizerApoConfig.BuildLoudifyConfig(new LoudifySettings { GainDb = 6 }, LimiterPath);
            Assert.Contains(" Gain 0.25 ", config);
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
