using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Records an area as an MP4, with sound if asked: pictures and sound are encoded and written while it records.</summary>
internal sealed class VideoJob : RecordingJob
{
    public const int FramesPerSecond = 30;
    public const int MaxSide = 1920;  // a larger area is shrunk to this
    private const int MaxMinutes = 30; // the video goes to disk as it is made, so this is about patience, not memory

    private readonly Rectangle _region;
    private readonly RecordingOptions _options;
    private readonly Stopwatch _clock = new();
    private Mp4Writer? _writer;
    private RegionGrabber? _grabber;
    private AudioRecorder? _audio;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private string? _path;

    public VideoJob(Rectangle region, RecordingOptions options)
    {
        _region = region;
        _options = options;
    }

    public override string Label => "video";

    public override string Extension => ".mp4";

    public override string FileFilter => "Video MP4 (*.mp4)|*.mp4";

    public override TimeSpan MaxDuration => TimeSpan.FromMinutes(MaxMinutes);

    public override TimeSpan Elapsed => _clock.Elapsed;

    public override IReadOnlyList<AudioMeter> Meters => _audio?.Meters ?? [];

    public override event Action<RecordingEnd>? LimitReached;

    public override void Start(string folder)
    {
        (int width, int height) = RegionGrabber.FitSize(_region, MaxSide, even: true);
        _path = NewFilePath(folder);

        var inputs = new List<AudioInput>();
        try
        {
            if (_options.Microphone)
            {
                inputs.Add(AudioInput.Microphone(_options.MicrophoneId));
            }
            if (_options.SystemSound)
            {
                inputs.Add(AudioInput.SystemSound());
            }
        }
        catch
        {
            foreach (AudioInput input in inputs)
            {
                input.Dispose();
            }
            throw;
        }

        _writer = new Mp4Writer(_path, width, height, FramesPerSecond, withAudio: inputs.Count > 0);
        _grabber = new RegionGrabber(_region, width, height);
        if (inputs.Count > 0)
        {
            _audio = new AudioRecorder(inputs, _writer, _clock);
        }

        // Windows wakes a sleeping thread every 15 ms unless asked for better; at 30 pictures a second that is too coarse.
        TimeBeginPeriod(1);
        _clock.Start();
        _writer.Start();
        _audio?.Start();
        _thread = new Thread(Capture) { IsBackground = true, Name = "Zarpazo video" };
        _thread.Start();
    }

    public override async Task<string?> FinishAsync()
    {
        StopCapture();
        if (_writer == null)
        {
            return null;
        }

        await _writer.FinishAsync();
        return _path;
    }

    public override void Abort()
    {
        StopCapture();
        string? path = _path;
        Mp4Writer? writer = _writer;
        if (writer == null || path == null)
        {
            return;
        }

        // The file has to be closed before it can be deleted.
        _ = Task.Run(async () =>
        {
            try
            {
                await writer.FinishAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // It is being thrown away anyway.
            }
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        });
    }

    private void StopCapture()
    {
        _stopRequested = true;
        _thread?.Join();
        _audio?.Dispose();
        _grabber?.Dispose();
        _clock.Stop();
        TimeEndPeriod(1);
    }

    private void Capture()
    {
        RegionGrabber grabber = _grabber!;
        long frame = 0;
        while (!_stopRequested)
        {
            long due = frame * 1000 / FramesPerSecond;
            long now = _clock.ElapsedMilliseconds;
            if (now < due)
            {
                Thread.Sleep(1);
                continue;
            }

            // The array goes into the encoder's queue and stays there, so every picture needs its own.
            var pixels = new byte[grabber.Width * grabber.Height * 4];
            grabber.Grab(pixels);
            _writer!.AddVideo(pixels, TimeSpan.FromMilliseconds(now)); // dropped, not queued, if the encoder is a second behind

            // After a slow picture the next ones are not rushed: the schedule restarts from where the clock is.
            frame = Math.Max(frame + 1, now * FramesPerSecond / 1000 + 1);

            if (now >= MaxDuration.TotalMilliseconds)
            {
                LimitReached?.Invoke(RecordingEnd.TimeLimit);
                return;
            }
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
