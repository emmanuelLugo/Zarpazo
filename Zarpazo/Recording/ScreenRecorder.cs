using System.Drawing;

namespace Zarpazo.Recording;

/// <summary>Takes pictures of a part of the screen at a steady pace on its own thread, keeping only what changed between them (for a GIF).</summary>
internal sealed class ScreenRecorder
{
    public const int DefaultFramesPerSecond = 12; // plenty for showing how something is done, and it keeps the file small
    public const int MaxSide = 1280;       // a larger area is shrunk to this: a full-screen GIF would be enormous
    private const int BytesPerPixel = 4;

    private readonly Rectangle _region;
    private readonly TimeSpan _maxDuration;
    private readonly long _maxBytes;
    private readonly RecordingEffects? _effects;
    private readonly RecordingClock _clock = new();
    private readonly List<RecordedFrame> _frames = [];
    private Thread? _thread;
    private volatile bool _stopRequested;
    private long _bytes;

    /// <param name="region">The part of the screen, in physical pixels.</param>
    public ScreenRecorder(Rectangle region, TimeSpan maxDuration, long maxBytes, RecordingEffects? effects = null, int framesPerSecond = DefaultFramesPerSecond)
    {
        _region = region;
        FramesPerSecond = framesPerSecond;
        _effects = effects;
        _maxDuration = maxDuration;
        _maxBytes = maxBytes;
        (Width, Height) = RegionGrabber.FitSize(region, MaxSide, even: false);
    }

    /// <summary>How many pictures are taken each second.</summary>
    public int FramesPerSecond { get; }

    /// <summary>The size of the pictures it produces.</summary>
    public int Width { get; }

    public int Height { get; }

    public TimeSpan Elapsed => _clock.Elapsed;

    public bool IsPaused => _clock.IsPaused;

    public void Pause() => _clock.Pause();

    public void Resume() => _clock.Resume();

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
        using var grabber = new RegionGrabber(_region, Width, Height, _effects);
        byte[] current = new byte[Width * Height * BytesPerPixel];
        byte[]? previous = null;
        int interval = 1000 / FramesPerSecond;
        long next = 0;

        while (!_stopRequested)
        {
            if (_clock.IsPaused)
            {
                Thread.Sleep(20);
                continue;
            }

            long now = _clock.ElapsedMilliseconds;
            if (now < next)
            {
                Thread.Sleep((int)(next - now));
                continue;
            }
            // A slow frame doesn't make the following ones rush to catch up.
            next = Math.Max(next + interval, now);

            grabber.Grab(current, now);
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
}
