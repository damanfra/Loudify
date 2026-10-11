using System.IO.MemoryMappedFiles;
using Loudify.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Loudify;

/// <summary>
/// Listens to the microphone (shared mode, so Discord and games keep using it)
/// and lowers the game while the user talks. The attenuation goes to the limiter
/// plugin through shared memory, which it reads on every audio block.
/// </summary>
internal sealed class MicDucking : IDisposable
{
    private readonly object _lock = new();
    private readonly VoiceDucker _ducker = new();
    private readonly System.Threading.Timer _restartTimer;
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private long _nextOpenAttempt;
    private float[] _samples = Array.Empty<float>();
    private bool _disposed;

    public MicDucking(double thresholdDb, double duckDb)
    {
        _ducker.ThresholdDb = thresholdDb;
        _ducker.DuckDb = duckDb;
        _restartTimer = new System.Threading.Timer(_ => Start(), null, Timeout.Infinite, Timeout.Infinite);
        Start();
    }

    /// <summary>Last microphone level in dBFS; read by the settings window meter.</summary>
    public double LevelDb { get; private set; } = -120;
    public bool Speaking => _ducker.Speaking;
    public string? MicrophoneName { get; private set; }
    public string? Error { get; private set; }
    /// <summary>Whether the last write reached the plugin. False while no audio has played
    /// since Equalizer APO started, or if the shared memory cannot be opened.</summary>
    public bool PluginConnected { get; private set; }

    public void Configure(double thresholdDb, double duckDb)
    {
        lock (_lock)
        {
            _ducker.ThresholdDb = thresholdDb;
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
                _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                MicrophoneName = _device.FriendlyName;
                _capture = new WasapiCapture(_device, useEventSync: true, audioBufferMillisecondsLength: 20);
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

    private void ScheduleRestart()
    {
        if (!_disposed)
            _restartTimer.Change(5000, Timeout.Infinite);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _capture == null)
                return;
            var format = _capture.WaveFormat;
            int count = ToFloat(e.Buffer, e.BytesRecorded, format.BitsPerSample);
            if (count == 0)
                return;

            double level = VoiceDucker.LevelDb(_samples.AsSpan(0, count));
            double blockMs = 1000.0 * count / format.Channels / format.SampleRate;
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
