using Loudify.Core;

namespace Loudify.Core.Tests;

public class UpdateCheckTests
{
    private const string Release = """
        {
          "tag_name": "v0.1.0.7",
          "html_url": "https://github.com/damanfra/Loudify/releases/tag/v0.1.0.7",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt", "size": 1 },
            {
              "name": "Loudify.exe",
              "browser_download_url": "https://github.com/damanfra/Loudify/releases/download/v0.1.0.7/Loudify.exe",
              "size": 71691564,
              "digest": "sha256:11467DB554876058EADF6D8FEB5F2A229B75393CDA245D16F780D30955240785"
            }
          ]
        }
        """;

    [Fact]
    public void ParsesTheExeAssetOfALatestRelease()
    {
        var release = UpdateCheck.ParseLatestRelease(Release);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 1, 0, 7), release.Version);
        Assert.Equal("v0.1.0.7", release.Tag);
        Assert.Equal("https://github.com/damanfra/Loudify/releases/download/v0.1.0.7/Loudify.exe", release.DownloadUrl);
        Assert.Equal(71691564, release.Size);
        Assert.Equal("11467db554876058eadf6d8feb5f2a229b75393cda245d16f780d30955240785", release.Sha256);
        Assert.Equal("https://github.com/damanfra/Loudify/releases/tag/v0.1.0.7", release.PageUrl);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "tag_name": "v1.0", "assets": [] }""")]
    [InlineData("""{ "tag_name": "nightly", "assets": [ { "name": "Loudify.exe", "browser_download_url": "https://x/Loudify.exe" } ] }""")]
    [InlineData("""{ "tag_name": "v1.0", "prerelease": true, "assets": [ { "name": "Loudify.exe", "browser_download_url": "https://x/Loudify.exe" } ] }""")]
    [InlineData("""{ "tag_name": "v1.0", "assets": [ { "name": "Loudify.exe", "browser_download_url": "http://x/Loudify.exe" } ] }""")]
    public void UnusableReleasesAreIgnored(string json)
    {
        Assert.Null(UpdateCheck.ParseLatestRelease(json));
    }

    [Fact]
    public void MissingDigestIsAllowed()
    {
        var release = UpdateCheck.ParseLatestRelease(
            """{ "tag_name": "0.2.0", "assets": [ { "name": "loudify.exe", "browser_download_url": "https://x/Loudify.exe" } ] }""");

        Assert.NotNull(release);
        Assert.Null(release.Sha256);
        Assert.Equal(UpdateCheck.ReleasesPageUrl, release.PageUrl);
    }

    [Theory]
    [InlineData("v0.1.0.8", "0.1.0.7", true)]
    [InlineData("v0.2.0", "0.1.0.99", true)]
    [InlineData("v0.2.0", "0.2.0.0", false)]
    [InlineData("v0.1.0.7", "0.1.0.7", false)]
    [InlineData("v0.1.0.6", "0.1.0.7", false)]
    public void ComparesVersionsWithMissingComponentsAsZero(string tag, string current, bool newer)
    {
        Assert.True(UpdateCheck.TryParseVersion(tag, out var version));
        var release = new ReleaseInfo(version, tag, "https://x", 0, null, "https://x");

        Assert.Equal(newer, UpdateCheck.IsNewer(release, Version.Parse(current)));
    }

    [Theory]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("0.1.0.7", "0.1.0.7")]
    [InlineData("1.0", "1.0.0")]
    public void FormatsVersions(string version, string expected)
    {
        Assert.Equal(expected, UpdateCheck.Format(Version.Parse(version)));
    }
}
