using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Records an area as an MP4, with sound if asked: pictures and sound are encoded and written while it records.</summary>
internal sealed class VideoJob : RecordingJob
{
    public const int MaxSide = 1920;  // a larger area is shrunk to this

    private readonly Rectangle _region;
    private readonly RecordingOptions _options;
    private readonly int _audioOffsetMs;
    private readonly RecordingQuality _quality;
    private readonly RecordingClock _clock = new();
    private Mp4Writer? _writer;
    private RegionGrabber? _grabber;
    private FrameInputState? _input;
    private CursorRecorder? _cursor;
    private AudioRecorder? _audio;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private bool _timerRaised;
    private string? _path;
    private byte[]? _firstPicture;
    private int _width;
    private int _height;

    /// <param name="audioOffsetMs">Moves the sound against the picture: negative plays it earlier, positive later.</param>
    public VideoJob(Rectangle region, RecordingOptions options, int audioOffsetMs = 0, RecordingQuality? quality = null)
    {
        _quality = quality ?? RecordingQuality.Default;
        _region = region;
        _options = options;
        _audioOffsetMs = audioOffsetMs;
    }

    public override string Label => "video";

    public override string Extension => ".mp4";

    public override bool CanEdit => true;

    public override string FileFilter => "Video MP4 (*.mp4)|*.mp4";

    public override TimeSpan MaxDuration => TimeSpan.FromMinutes(_quality.VideoMaxMinutes); // the video goes to disk as it is made, so this is about patience, not memory

    public override TimeSpan Elapsed => _clock.Elapsed;

    public override IReadOnlyList<AudioMeter> Meters => _audio?.Meters ?? [];

    public override event Action<RecordingEnd>? LimitReached;

    public override bool IsPaused => _clock.IsPaused;

    public override void Prepare(string folder)
    {
        (int width, int height) = RegionGrabber.FitSize(_region, MaxSide, even: true);
        (_width, _height) = (width, height);
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
                inputs.Add(AudioInput.SystemSound(_options.SystemSoundId));
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

        _writer = new Mp4Writer(_path, width, height, _quality.VideoFramesPerSecond, withAudio: inputs.Count > 0, _quality.BitsPerPixelPerFrame);
        // The effects and the pointer's record read the mouse together, so neither misses a quick click the other saw.
        _input = new FrameInputState(new WindowsInputState());
        _grabber = new RegionGrabber(_region, width, height, RecordingEffects.FromOptions(_options, _input));
        _cursor = new CursorRecorder(_input, _region, width, height);
        if (inputs.Count > 0)
        {
            _audio = new AudioRecorder(inputs, _writer, _clock, _audioOffsetMs);
        }

    }

    public override void Begin()
    {
        // Windows wakes a sleeping thread every 15 ms unless asked for better; at 30 pictures a second that is too coarse.
        TimeBeginPeriod(1);
        _timerRaised = true;
        _clock.Start();
        _writer!.Start();
        _audio?.Start();
        _thread = new Thread(Capture) { IsBackground = true, Name = "Zarpazo video" };
        _thread.Start();
    }

    public override void SetSourceGain(int source, float gain) => _audio?.SetGain(source, gain);

    public override void Pause() => _clock.Pause();

    public override void Resume()
    {
        _audio?.DiscardBuffered(); // what the devices heard during the pause is not part of the video
        _clock.Resume();
    }

    public override async Task<string?> FinishAsync()
    {
        StopCapture();
        if (_writer == null)
        {
            return null;
        }

        await _writer.FinishAsync();
        SaveCursor();
        if (_firstPicture != null)
        {
            Thumbnail = PictureOf(_firstPicture, _width, _height);
            _firstPicture = null;
        }
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

    // Only the zoom of the editor needs it, so a video without it is still a good video.
    private void SaveCursor()
    {
        try
        {
            _cursor?.ToTrack(_clock.ElapsedMilliseconds).Save(_path!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void StopCapture()
    {
        _stopRequested = true;
        _thread?.Join();
        _audio?.Dispose();
        _grabber?.Dispose();
        _clock.Stop();
        if (_timerRaised)
        {
            TimeEndPeriod(1);
            _timerRaised = false;
        }
    }

    private void Capture()
    {
        RegionGrabber grabber = _grabber!;
        long frame = 0;
        while (!_stopRequested)
        {
            if (_clock.IsPaused)
            {
                Thread.Sleep(10);
                continue;
            }

            long due = frame * 1000 / _quality.VideoFramesPerSecond;
            long now = _clock.ElapsedMilliseconds;
            if (now < due)
            {
                Thread.Sleep(1);
                continue;
            }

            // The array goes into the encoder's queue and comes back for reuse once it has been encoded.
            byte[] pixels = _writer!.RentPicture();
            _input!.Refresh();
            grabber.Grab(pixels, now);
            _cursor!.Update(now);
            _firstPicture ??= (byte[])pixels.Clone(); // the history shows the recording by this picture
            _writer!.AddVideo(pixels, TimeSpan.FromMilliseconds(now)); // dropped, not queued, if the encoder is a second behind

            // After a slow picture the next ones are not rushed: the schedule restarts from where the clock is.
            frame = Math.Max(frame + 1, now * _quality.VideoFramesPerSecond / 1000 + 1);

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
