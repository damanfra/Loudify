namespace Loudify;

/// <summary>The app icon (also the exe icon), loaded with all its sizes so the
/// title bar gets a sharp 16 px image instead of a scaled-down 32 px one.</summary>
internal static class AppIcon
{
    public static Icon? Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Loudify.ico");
        return stream != null ? new Icon(stream) : null;
    }
}
