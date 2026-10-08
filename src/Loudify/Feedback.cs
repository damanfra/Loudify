using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;

namespace Loudify;

/// <summary>Tray icons and the short tones played when the boost is toggled.
/// Tones matter in fullscreen games, where the tray is not visible.</summary>
internal sealed class Feedback : IDisposable
{
    private readonly SoundPlayer _onSound = new(CreateTone(660, 990));
    private readonly SoundPlayer _offSound = new(CreateTone(990, 660));

    public Icon OnIcon { get; } = CreateIcon(Color.FromArgb(0x1E, 0x88, 0xE5), waves: 2);
    public Icon OffIcon { get; } = CreateIcon(Color.FromArgb(0x75, 0x75, 0x75), waves: 0);
    public Icon ErrorIcon { get; } = CreateIcon(Color.FromArgb(0xE5, 0x39, 0x35), waves: 0);

    public void PlayToggle(bool enabled)
    {
        try
        {
            (enabled ? _onSound : _offSound).Play();
        }
        catch (InvalidOperationException)
        {
            // No playback device; the tone is optional.
        }
    }

    public void Dispose()
    {
        _onSound.Dispose();
        _offSound.Dispose();
        DestroyIcon(OnIcon);
        DestroyIcon(OffIcon);
        DestroyIcon(ErrorIcon);
    }

    /// <summary>Two 70 ms tones (16-bit mono WAV in memory), quiet and with fades.</summary>
    private static MemoryStream CreateTone(double firstHz, double secondHz)
    {
        const int sampleRate = 44100;
        const int toneSamples = sampleRate * 70 / 1000;
        const int fade = sampleRate * 5 / 1000;
        const double amplitude = 0.15 * short.MaxValue;
        int samples = toneSamples * 2;

        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + samples * 2);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sampleRate);
            w.Write(sampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(samples * 2);
            for (int i = 0; i < samples; i++)
            {
                int n = i % toneSamples;
                double hz = i < toneSamples ? firstHz : secondHz;
                double envelope = Math.Min(1.0, Math.Min(n, toneSamples - n) / (double)fade);
                w.Write((short)(amplitude * envelope * Math.Sin(2 * Math.PI * hz * n / sampleRate)));
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static Icon CreateIcon(Color color, int waves)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            // Speaker body and cone.
            g.FillRectangle(brush, 3, 11, 7, 10);
            g.FillPolygon(brush, new[] { new Point(9, 11), new Point(17, 4), new Point(17, 28), new Point(9, 21) });

            for (int i = 0; i < waves; i++)
            {
                int r = 6 + i * 6;
                g.DrawArc(pen, 17 - r, 16 - r, 2 * r, 2 * r, -45, 90);
            }
            if (waves == 0)
            {
                g.DrawLine(pen, 21, 11, 29, 21);
                g.DrawLine(pen, 29, 11, 21, 21);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DestroyIcon(Icon icon)
    {
        IntPtr handle = icon.Handle;
        icon.Dispose();
        DestroyIcon(handle);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
