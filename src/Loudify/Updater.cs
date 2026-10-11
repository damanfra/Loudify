using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Loudify.Core;

namespace Loudify;

/// <summary>
/// Downloads a release next to the running exe and swaps it in. Windows lets a
/// running exe be renamed but not overwritten, so the current file is moved to
/// Loudify.exe.old, the new one takes its name, and the new process deletes the
/// old file once this one has exited.
/// </summary>
internal static class Updater
{
    public const string AfterUpdateArgument = "--after-update";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion =>
        UpdateCheck.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Não foi possível localizar o Loudify.exe em execução.");

    public static async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateCheck.LatestReleaseApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancellationToken);
        // 404: the repository has no published release yet.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return UpdateCheck.ParseLatestRelease(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Downloads the release to Loudify.exe.new and checks its SHA-256.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        string target = ExePath + ".new";
        try
        {
            using var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                }
            }

            if (release.Size > 0 && written != release.Size)
                throw new InvalidDataException($"Download incompleto ({written} de {release.Size} bytes).");
            string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (release.Sha256 != null && actual != release.Sha256)
                throw new InvalidDataException("O arquivo baixado não confere com a release (SHA-256 diferente).");
            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>Puts the downloaded exe in place and starts it. The caller must exit
    /// right after, so the new process can take over.</summary>
    public static void InstallAndRestart(string downloadedPath)
    {
        string exe = ExePath;
        string old = exe + ".old";
        File.Move(exe, old, overwrite: true);
        try
        {
            File.Move(downloadedPath, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add(AfterUpdateArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start);
    }

    /// <summary>Called by the new process: waits for the old one to exit so the
    /// single-instance mutex is free, then removes leftovers of the swap.</summary>
    public static void FinishUpdate(int previousProcessId)
    {
        try
        {
            using var previous = Process.GetProcessById(previousProcessId);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
        CleanUp();
    }

    public static void CleanUp()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        TryDelete(exe + ".old");
        TryDelete(exe + ".new");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Loudify", UpdateCheck.Format(CurrentVersion)));
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
