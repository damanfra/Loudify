using System.Text.Json;

namespace Loudify.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public LoudifySettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var settings = JsonSerializer.Deserialize<LoudifySettings>(File.ReadAllText(Path), JsonOptions);
                if (settings != null)
                    return settings.Sanitized();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new LoudifySettings();
    }

    public void Save(LoudifySettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(settings.Sanitized(), JsonOptions));
    }
}
