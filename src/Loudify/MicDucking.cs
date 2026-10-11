using System.IO.MemoryMappedFiles;
using Loudify.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Loudify;

/// <summary>
/// Listens to the microphone (shared mode, so Discord and games keep using it)
/// and lowers the game while the user talks. The attenuation goes to the limiter
/// plugin through shared memory, which it reads on every audio block.
/// </summary>
internal sealed class MicDucking : IDisposable, IMMNotificationClient
{
    private readonly object _lock = new();
    // Kept only for device notifications (unplug, default device changed).
    private readonly MMDeviceEnumerator _notifications = new();
    private readonly VoiceDucker _ducker = new();
    private readonly System.Threading.Timer _restartTimer;
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private long _nextOpenAttempt;
    private float[] _samples = Array.Empty<float>();
    private VoiceBandFilter? _filter;
    private bool _disposed;

    /// <param name="deviceId">A capture endpoint id, or null for the Windows default communications microphone.</param>
    public MicDucking(string? deviceId, double marginDb, double duckDb)
    {
        DeviceId = deviceId;
        _ducker.MarginDb = marginDb;
        _ducker.DuckDb = duckDb;
        _restartTimer = new System.Threading.Timer(_ => Start(), null, Timeout.Infinite, Timeout.Infinite);
        _notifications.RegisterEndpointNotificationCallback(this);
        Start();
    }

    public string? DeviceId { get; }

    /// <summary>Active microphones, for the settings window.</summary>
    public static List<(string Id, string Name)> ListMicrophones()
    {
        var list = new List<(string, string)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    list.Add((device.ID, device.FriendlyName));
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        return list;
    }

    /// <summary>Last microphone level in dBFS (speech band only); read by the settings window meter.</summary>
    public double LevelDb { get; private set; } = -120;
    public double? NoiseFloorDb => _ducker.NoiseFloorDb;
    public double ThresholdDb => _ducker.ThresholdDb;
    public bool Speaking => _ducker.Speaking;
    public string? MicrophoneName { get; private set; }
    public string? Error { get; private set; }
    /// <summary>Whether the last write reached the plugin. False while no audio has played
    /// since Equalizer APO started, or if the shared memory cannot be opened.</summary>
    public bool PluginConnected { get; private set; }

    public void Configure(double marginDb, double duckDb)
    {
        lock (_lock)
        {
            _ducker.MarginDb = marginDb;
            _ducker.DuckDb = duckDb;
        }
    }

    public void Dispose()
    {
        (WasapiCapture?, MMDevice?) old;
        lock (_lock)
        {
            _disposed = true;
            _restartTimer.Dispose();
            old = DetachCapture();
        }
        try
        {
            _notifications.UnregisterEndpointNotificationCallback(this);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        _notifications.Dispose();
        ReleaseCapture(old);
        lock (_lock)
        {
            WriteDuck(0);
            _view?.Dispose();
            _mapping?.Dispose();
            _view = null;
            _mapping = null;
        }
    }

    private void Start()
    {
        (WasapiCapture?, MMDevice?) old;
        lock (_lock)
            old = DetachCapture();
        ReleaseCapture(old);

        (WasapiCapture?, MMDevice?) failed = (null, null);
        lock (_lock)
        {
            if (_disposed)
                return;
            try
            {
                // A fresh enumerator each time: restarts run on a thread-pool thread.
                using var enumerator = new MMDeviceEnumerator();
                // The communications device is the one Discord and games use for voice.
                _device = DeviceId == null
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : enumerator.GetDevice(DeviceId);
                if (_device.State != DeviceState.Active)
                    throw new InvalidOperationException("o microfone escolhido está desconectado.");
                MicrophoneName = _device.FriendlyName;
                _capture = new WasapiCapture(_device, useEventSync: true, audioBufferMillisecondsLength: 20);
                _filter = new VoiceBandFilter(_capture.WaveFormat.SampleRate);
                _capture.DataAvailable += OnData;
                _capture.RecordingStopped += OnStopped;
                _capture.StartRecording();
                Error = null;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                or UnauthorizedAccessException or ArgumentException)
            {
                failed = DetachCapture();
                MicrophoneName = null;
                Error = "Microfone indisponível: " + ex.Message;
                ScheduleRestart();
            }
        }
        ReleaseCapture(failed);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        // Device unplugged or changed: try again shortly.
        if (e.Exception != null)
        {
            Error = "Microfone desconectado. Tentando de novo...";
            ScheduleRestart();
        }
    }

    private void ScheduleRestart(int delayMs = 5000)
    {
        try
        {
            if (!_disposed)
                _restartTimer.Change(delayMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // Device notifications arrive on a COM thread; they only schedule a restart.

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (DeviceId == null && flow == DataFlow.Capture && role == Role.Communications)
            ScheduleRestart(500);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        // The chosen microphone came back, or the current one went away.
        if (Error != null || string.Equals(deviceId, DeviceId, StringComparison.OrdinalIgnoreCase))
            ScheduleRestart(500);
    }

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        if (Error != null)
            ScheduleRestart(500);
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _capture == null)
                return;
            var format = _capture.WaveFormat;
            int count = ToFloat(e.Buffer, e.BytesRecorded, format.BitsPerSample);
            int frames = MixToMono(count, format.Channels);
            if (frames == 0 || _filter == null)
                return;

            // Detect on the speech band only, so fans and hum do not count as talking.
            var mono = _samples.AsSpan(0, frames);
            _filter.Process(mono);
            double level = VoiceDucker.LevelDb(mono);
            double blockMs = 1000.0 * frames / format.SampleRate;
            LevelDb = level;
            WriteDuck(_ducker.Process(level, blockMs));
        }
    }

    /// <summary>Converts the captured bytes into _samples. Shared-mode WASAPI delivers
    /// 32-bit float; 16-bit PCM is handled just in case.</summary>
    private int ToFloat(byte[] buffer, int bytes, int bitsPerSample)
    {
        int count = bitsPerSample switch { 32 => bytes / 4, 16 => bytes / 2, _ => 0 };
        if (_samples.Length < count)
            _samples = new float[count];
        if (bitsPerSample == 32)
            Buffer.BlockCopy(buffer, 0, _samples, 0, count * 4);
        else
            for (int i = 0; i < count; i++)
                _samples[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
        return count;
    }

    /// <summary>Averages interleaved channels in place into the start of _samples.</summary>
    private int MixToMono(int count, int channels)
    {
        if (channels <= 1)
            return count;
        int frames = count / channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += _samples[f * channels + c];
            _samples[f] = sum / channels;
        }
        return frames;
    }

    private void WriteDuck(double duckDb)
    {
        PluginConnected = false;
        var view = OpenView();
        if (view == null)
            return;
        try
        {
            if (view.ReadUInt32(ControlProtocol.MagicOffset) != ControlProtocol.Magic
                || view.ReadUInt32(ControlProtocol.VersionOffset) != ControlProtocol.Version)
                return;
            view.Write(ControlProtocol.DuckDbOffset, (float)duckDb);
            view.Write(ControlProtocol.HeartbeatOffset, (ulong)Environment.TickCount64);
            PluginConnected = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            CloseView();
        }
    }

    /// <summary>The plugin creates the shared memory when Equalizer APO loads it, which
    /// may happen after Loudify starts, so opening is retried once a second.</summary>
    private MemoryMappedViewAccessor? OpenView()
    {
        if (_view != null)
            return _view;
        long now = Environment.TickCount64;
        if (now < _nextOpenAttempt)
            return null;
        _nextOpenAttempt = now + 1000;
        try
        {
            _mapping = MemoryMappedFile.OpenExisting(ControlProtocol.MappingName, MemoryMappedFileRights.ReadWrite);
            _view = _mapping.CreateViewAccessor(0, ControlProtocol.Size, MemoryMappedFileAccess.ReadWrite);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            CloseView();
        }
        return _view;
    }

    private void CloseView()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
    }

    /// <summary>Unhooks the current capture under the lock. Disposing it waits for the
    /// capture thread, which may be blocked on the lock in OnData, so that happens in
    /// ReleaseCapture, outside the lock.</summary>
    private (WasapiCapture?, MMDevice?) DetachCapture()
    {
        var old = (_capture, _device);
        if (_capture != null)
        {
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnStopped;
        }
        _capture = null;
        _device = null;
        _ducker.Reset();
        LevelDb = -120;
        return old;
    }

    private static void ReleaseCapture((WasapiCapture? Capture, MMDevice? Device) old)
    {
        if (old.Capture != null)
        {
            try
            {
                old.Capture.StopRecording();
                old.Capture.Dispose();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
            }
        }
        old.Device?.Dispose();
    }
}
