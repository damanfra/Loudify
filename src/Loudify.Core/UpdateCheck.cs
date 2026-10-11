using System.Text.Json;

namespace Loudify.Core;

/// <summary>A published Loudify release that can replace the running exe.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string DownloadUrl, long Size, string? Sha256, string PageUrl);

/// <summary>
/// Reads the latest GitHub release and decides whether it is newer than the
/// running build. The download and the exe swap live in the app.
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/damanfra/Loudify/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/damanfra/Loudify/releases";
    public const string AssetName = "Loudify.exe";
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromDays(1);

    /// <summary>Returns the release described by the GitHub API response, or null when
    /// it is not a usable Loudify release (draft, prerelease, bad tag, no exe).</summary>
    public static ReleaseInfo? ParseLatestRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || IsTrue(root, "draft") || IsTrue(root, "prerelease")
                || GetString(root, "tag_name") is not { } tag
                || !TryParseVersion(tag, out var version)
                || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!string.Equals(GetString(asset, "name"), AssetName, StringComparison.OrdinalIgnoreCase)
                    || GetString(asset, "browser_download_url") is not { } url
                    || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;

                long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                string? sha256 = GetString(asset, "digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest["sha256:".Length..].ToLowerInvariant()
                    : null;
                return new ReleaseInfo(version, tag, url, size, sha256, GetString(root, "html_url") ?? ReleasesPageUrl);
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>Parses tags like "v0.1.0.7" or "0.2.0".</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        string text = (tag ?? "").Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var parsed))
            return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>Fills missing components with 0, so 0.2.0 equals 0.2.0.0 when compared.</summary>
    public static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

    public static bool IsAutomaticCheckDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is not { } last || now - last >= AutomaticInterval || last > now;

    public static string Format(Version v)
    {
        v = Normalize(v);
        return v.Revision == 0 ? v.ToString(3) : v.ToString(4);
    }

    private static bool IsTrue(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
