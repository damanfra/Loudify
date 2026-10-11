namespace Loudify.Core;

/// <summary>
/// Layout of the shared memory the limiter plugin creates inside audiodg.exe.
/// Must stay in sync with native/LoudifyLimiter/src/ControlChannel.h.
/// </summary>
public static class ControlProtocol
{
    public const string MappingName = @"Global\LoudifyControl";
    public const int Size = 24;
    public const uint Magic = 0x3159444C; // "LDY1"
    public const uint Version = 1;

    public const int MagicOffset = 0;
    public const int VersionOffset = 4;
    public const int DuckDbOffset = 8;
    public const int HeartbeatOffset = 16;

    /// <summary>The plugin ignores the duck when the heartbeat is older than this.</summary>
    public const int StaleMs = 500;
}
