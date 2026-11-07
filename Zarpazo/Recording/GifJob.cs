using System.Drawing;
using System.IO;

namespace Zarpazo.Recording;

/// <summary>Records an area as an animated GIF: the changes are kept in memory and the file is written when it stops.</summary>
internal sealed class GifJob : RecordingJob
{
    private const int MaxSeconds = 30;
    private const long MaxBytes = 400L * 1024 * 1024;

    private readonly ScreenRecorder _recorder;
    private string _folder = "";

    public GifJob(Rectangle region)
    {
        _recorder = new ScreenRecorder(region, TimeSpan.FromSeconds(MaxSeconds), MaxBytes);
    }

    public override string Label => "GIF";

    public override string Extension => ".gif";

    public override string FileFilter => "Imagen GIF (*.gif)|*.gif";

    public override TimeSpan MaxDuration => TimeSpan.FromSeconds(MaxSeconds);

    public override TimeSpan Elapsed => _recorder.Elapsed;

    public override event Action<RecordingEnd>? LimitReached
    {
        add => _recorder.LimitReached += value;
        remove => _recorder.LimitReached -= value;
    }

    public override bool IsPaused => _recorder.IsPaused;

    public override void Prepare(string folder) => _folder = folder;

    public override void Begin() => _recorder.Start();

    public override void Pause() => _recorder.Pause();

    public override void Resume() => _recorder.Resume();

    public override async Task<string?> FinishAsync()
    {
        (IReadOnlyList<RecordedFrame> frames, int durationMs) = _recorder.Stop();
        if (frames.Count == 0)
        {
            return null; // stopped before the first picture was taken
        }

        return await Task.Run(() =>
        {
            string path = NewFilePath(_folder);
            using FileStream file = File.Create(path);
            GifEncoder.Encode(frames, _recorder.Width, _recorder.Height, durationMs, file);
            return path;
        });
    }

    public override void Abort() => _recorder.Stop();
}
