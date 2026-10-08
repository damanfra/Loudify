using System.Security.Cryptography;
using Loudify.Core;
using Microsoft.Win32;

namespace Loudify;

/// <summary>
/// The local Equalizer APO installation. Its config directory is writable by
/// all users (the installer grants it), so no elevation is needed, and Equalizer
/// APO reloads the configuration as soon as a file in it changes.
/// </summary>
internal sealed class EqualizerApoInstallation
{
    public const string DownloadUrl = "https://sourceforge.net/projects/equalizerapo/files/latest/download";
    private const string LimiterResourceName = "LoudifyLimiter.dll";

    private EqualizerApoInstallation(string configPath)
    {
        ConfigPath = configPath;
    }

    public string ConfigPath { get; }

    public static EqualizerApoInstallation? Detect()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO");
        if (key?.GetValue("ConfigPath") is not string path || !Directory.Exists(path))
            return null;
        return new EqualizerApoInstallation(path);
    }

    /// <summary>
    /// Copies the embedded limiter plugin next to the configuration and returns its path.
    /// The file name carries a content hash: a DLL loaded by the audio engine cannot be
    /// overwritten, so a new build gets a new file and stale ones are removed when unused.
    /// </summary>
    public string DeployLimiter()
    {
        using var stream = typeof(EqualizerApoInstallation).Assembly.GetManifestResourceStream(LimiterResourceName)
            ?? throw new InvalidOperationException("Este build do Loudify não contém o limitador (LoudifyLimiter.dll).");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();

        string hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        string dir = Path.Combine(ConfigPath, "Loudify");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, $"LoudifyLimiter-{hash}.dll");

        if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length)
            AtomicFile.WriteAllBytes(target, bytes);

        foreach (string old in Directory.GetFiles(dir, "LoudifyLimiter-*.dll"))
        {
            if (string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still loaded by the audio engine; removed on a later start.
            }
        }
        return target;
    }

    public void EnsureIncluded()
    {
        string configFile = Path.Combine(ConfigPath, EqualizerApoConfig.ConfigFileName);
        string? current = File.Exists(configFile) ? File.ReadAllText(configFile) : null;
        string? updated = EqualizerApoConfig.WithInclude(current);
        if (updated == null)
            return;

        // The shipped default config is replaced rather than extended; keep a copy.
        if (!string.IsNullOrWhiteSpace(current) && !updated.StartsWith(current, StringComparison.Ordinal))
        {
            File.Copy(configFile, Path.Combine(ConfigPath, EqualizerApoConfig.BackupFileName), overwrite: true);
        }
        AtomicFile.WriteAllText(configFile, updated);
    }

    public void WriteLoudifyConfig(string contents)
    {
        string path = Path.Combine(ConfigPath, EqualizerApoConfig.LoudifyFileName);
        if (File.Exists(path) && File.ReadAllText(path) == contents)
            return;
        AtomicFile.WriteAllText(path, contents);
    }
}
