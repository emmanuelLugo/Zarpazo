using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Takes pictures of a part of the screen at a steady pace on its own thread, keeping only what changed between them.</summary>
internal sealed class ScreenRecorder
{
    public const int FramesPerSecond = 12; // plenty for showing how something is done, and it keeps the file small
    public const int MaxSide = 1280;       // a larger area is shrunk to this: a full-screen GIF would be enormous
    private const int BytesPerPixel = 4;

    private readonly Rectangle _region;
    private readonly TimeSpan _maxDuration;
    private readonly long _maxBytes;
    private readonly Stopwatch _clock = new();
    private readonly List<RecordedFrame> _frames = [];
    private Thread? _thread;
    private volatile bool _stopRequested;
    private long _bytes;

    /// <param name="region">The part of the screen, in physical pixels.</param>
    public ScreenRecorder(Rectangle region, TimeSpan maxDuration, long maxBytes)
    {
        _region = region;
        _maxDuration = maxDuration;
        _maxBytes = maxBytes;

        double scale = Math.Min(1, (double)MaxSide / Math.Max(region.Width, region.Height));
        Width = Math.Max(1, (int)Math.Round(region.Width * scale));
        Height = Math.Max(1, (int)Math.Round(region.Height * scale));
    }

    /// <summary>The size of the pictures it produces.</summary>
    public int Width { get; }

    public int Height { get; }

    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>Raised, from the capture thread, when it stops by itself because a limit was reached.</summary>
    public event Action<RecordingEnd>? LimitReached;

    public void Start()
    {
        _clock.Start();
        _thread = new Thread(Run) { IsBackground = true, Name = "Zarpazo recorder" };
        _thread.Start();
    }

    /// <summary>Ends the recording and hands over what was captured, with its total length in milliseconds.</summary>
    public (IReadOnlyList<RecordedFrame> Frames, int DurationMs) Stop()
    {
        _stopRequested = true;
        _thread?.Join();
        _clock.Stop();
        return (_frames, (int)_clock.ElapsedMilliseconds);
    }

    private void Run()
    {
        using var full = new Bitmap(_region.Width, _region.Height, PixelFormat.Format32bppArgb);
        using Bitmap? small = Width != _region.Width || Height != _region.Height ? new Bitmap(Width, Height, PixelFormat.Format32bppArgb) : null;
        byte[] current = new byte[Width * Height * BytesPerPixel];
        byte[]? previous = null;
        const int interval = 1000 / FramesPerSecond;
        long next = 0;

        while (!_stopRequested)
        {
            long now = _clock.ElapsedMilliseconds;
            if (now < next)
            {
                Thread.Sleep((int)(next - now));
                continue;
            }
            // A slow frame doesn't make the following ones rush to catch up.
            next = Math.Max(next + interval, now);

            Capture(full, small, current);
            if (previous == null)
            {
                Keep(new RecordedFrame(0, 0, Width, Height, (byte[])current.Clone(), (int)now));
                previous = current;
                current = new byte[current.Length];
            }
            else if (FrameDiff.ChangedRect(previous, current, Width, Height) is { } changed)
            {
                Keep(new RecordedFrame(changed.X, changed.Y, changed.Width, changed.Height,
                    FrameDiff.Crop(current, Width, changed.X, changed.Y, changed.Width, changed.Height), (int)now));
                (previous, current) = (current, previous);
            }

            if (_bytes > _maxBytes)
            {
                LimitReached?.Invoke(RecordingEnd.MemoryLimit);
                return;
            }
            if (now >= _maxDuration.TotalMilliseconds)
            {
                LimitReached?.Invoke(RecordingEnd.TimeLimit);
                return;
            }
        }
    }

    private void Keep(RecordedFrame frame)
    {
        _frames.Add(frame);
        _bytes += frame.Pixels.Length;
    }

    private void Capture(Bitmap full, Bitmap? small, byte[] into)
    {
        using (Graphics graphics = Graphics.FromImage(full))
        {
            graphics.CopyFromScreen(_region.Location, Point.Empty, _region.Size);
            CursorPainter.Draw(graphics, _region.Location);
        }

        Bitmap source = full;
        if (small != null)
        {
            using Graphics graphics = Graphics.FromImage(small);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(full, 0, 0, Width, Height);
            source = small;
        }

        BitmapData data = source.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Scan0, into, 0, into.Length); // 32 bits per pixel: the rows have no padding
        }
        finally
        {
            source.UnlockBits(data);
        }
    }
}
