using System.IO;
using System.Runtime.InteropServices;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Zarpazo.Recording;

/// <summary>
/// Writes a video again with the zooms applied: every picture is decoded, the part the camera shows is stretched to the full
/// size, and the result is encoded with the frame rate and sound of the original, at its size or the crop's.
/// </summary>
internal static class ZoomRenderer
{
    private const double MinBitsPerPixel = 0.15;
    private const int MinFramesPerSecond = 30;

    private static readonly int[] StandardRates = [10, 12, 15, 20, 24, 25, 30, 48, 50, 60];

    /// <param name="camera">What each moment of <paramref name="source"/> shows; its times are the source's.</param>
    /// <param name="progress">Reports 0 to 1 while it works.</param>
    /// <exception cref="InvalidOperationException">Windows could not read or write the video.</exception>
    public static async Task RenderAsync(string source, string target, ZoomCamera camera, IProgress<double>? progress = null) =>
        await RenderAsync(source, target, camera, frame: null, progress);

    /// <param name="frame">The frame to put the recording in, with its camera's times and points in the scene; null for none.</param>
    /// <param name="cursor">The pointer to draw over every picture; null for none.</param>
    /// <param name="crop">The part of each picture that is kept; the video takes its size. The camera's views and the
    /// frame are then in shares of the cropped picture (see <see cref="CropArea"/>).</param>
    /// <param name="clock">When pieces of <paramref name="source"/> play faster or slower: which moment of it each moment of the
    /// result shows. The camera and the pointer are then on the result's clock, and those pieces lose their sound. Null plays
    /// the source as it is.</param>
    public static async Task RenderAsync(string source, string target, ZoomCamera camera, VideoFrameStyle? frame, IProgress<double>? progress = null,
        CursorLayer? cursor = null, VideoCrop crop = default, PlayClock? clock = null)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(source);
        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(file);
        Windows.Storage.FileProperties.VideoProperties info = await file.Properties.GetVideoPropertiesAsync();
        int sourceWidth = (int)profile.Video.Width & ~1;
        int sourceHeight = (int)profile.Video.Height & ~1;
        CropArea area = CropArea.For(crop, sourceWidth, sourceHeight);
        int width = area.Width;
        int height = area.Height;
        double recordedRate = VideoFrameReader.RecordedFrameRate(source)
            ?? profile.Video.FrameRate.Numerator / (double)Math.Max(1u, profile.Video.FrameRate.Denominator);
        // Recordings stamp their pictures in whole milliseconds (gaps of 33 and 34 ms make 29.4), so the rate is taken as the
        // nearest usual one.
        // Never fewer than 30: the camera moves every picture, so even over a recording with few pictures it glides.
        int fps = Math.Max(MinFramesPerSecond, StandardRates.MinBy(rate => Math.Abs(rate - recordedRate)));
        bool withAudio = profile.Audio != null;
        double duration = Math.Max(0.1, clock?.Duration ?? info.Duration.TotalSeconds);
        // At least the quality of the original, and never less than about 9 Mbps at 1080p: while the camera moves every pixel
        // changes, and at the 2 Mbps a mostly still screen recording gets, the zoom came out soft and blocky.
        double bitsPerPixel = Math.Clamp(profile.Video.Bitrate / ((double)sourceWidth * sourceHeight * fps), MinBitsPerPixel, 0.5);

        FrameCompositor? compositor = null;
        FrameLayout? layout = null;
        if (frame is { Enabled: true })
        {
            layout = FrameLayout.For(frame, width, height);
            compositor = new FrameCompositor(layout.Value, FrameBackdrop.Render(frame, layout.Value), sourceWidth, sourceHeight, area.Pixels);
        }

        var writer = new Mp4Writer(target, width, height, fps, withAudio, bitsPerPixel, live: false);
        writer.Start();
        using var cancel = new CancellationTokenSource();
        Exception? failure = null;

        // Pictures and sound are read by two readers side by side: the encoder takes them in time order, and a file can store
        // a second of one before the other, so one reader feeding both could wait for room that never comes.
        Task video = Task.Run(async () =>
        {
            try
            {
                // The recording's pictures are not evenly spaced (a 1080p screen takes about as long to copy as there is time
                // for each picture, so some come late), and a camera moved by their times moved in uneven steps. The result
                // has a picture exactly every 1/fps seconds instead: the camera where it is at that instant, over the latest
                // recorded picture.
                using var reader = new VideoFrameReader(source, sourceWidth, sourceHeight);
                var current = new byte[sourceWidth * sourceHeight * 4];
                var next = new byte[sourceWidth * sourceHeight * 4];
                if (!reader.TryRead(current, out TimeSpan currentTime))
                {
                    return;
                }
                bool hasNext = reader.TryRead(next, out TimeSpan nextTime);
                double frame = 1.0 / fps;
                for (long n = 0; ; n++)
                {
                    double t = n * frame;
                    if (clock != null && t >= clock.Duration)
                    {
                        break;
                    }
                    double at = clock?.ToInput(t) ?? t; // the moment of the source to show
                    while (hasNext && nextTime.TotalSeconds <= at + frame / 2)
                    {
                        (current, next) = (next, current);
                        currentTime = nextTime;
                        hasNext = reader.TryRead(next, out nextTime);
                    }
                    if (!hasNext && at >= currentTime.TotalSeconds + frame / 2)
                    {
                        break; // the last recorded picture has had its time on screen
                    }

                    byte[] picture = writer.RentPicture();
                    ZoomView view = camera.ViewAt(t);
                    if (compositor != null)
                    {
                        compositor.Render(current, picture, view);
                    }
                    else
                    {
                        ZoomResampler.Render(current, sourceWidth, sourceHeight, area.Pixels, picture, width, height, view);
                    }
                    cursor?.Draw(picture, width, height, t, view, layout);
                    await writer.AddVideoAsync(picture, TimeSpan.FromSeconds(t), cancel.Token);
                    progress?.Report(Math.Clamp(t / duration, 0, 1));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
                cancel.Cancel();
            }
        });

        Task sound = !withAudio ? Task.CompletedTask : Task.Run(async () =>
        {
            try
            {
                using var reader = new AudioChunkReader(source);
                double written = 0; // where the sound written so far ends, on the result's clock
                while (reader.Read(out TimeSpan time) is { } pcm)
                {
                    double at = Math.Max(0, time.TotalSeconds);
                    if (clock == null)
                    {
                        await writer.AddAudioAsync(pcm, TimeSpan.FromSeconds(at), cancel.Token);
                        continue;
                    }
                    if (clock.IsRetimedAt(at))
                    {
                        continue; // a faster or slower piece plays in silence, filled in below
                    }
                    double play = clock.ToPlay(at);
                    written = await AddSilenceAsync(writer, written, play, cancel.Token);
                    await writer.AddAudioAsync(pcm, TimeSpan.FromSeconds(play), cancel.Token);
                    written = Math.Max(written, play + SecondsOf(pcm.Length));
                }
                if (clock != null)
                {
                    await AddSilenceAsync(writer, written, clock.Duration, cancel.Token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
                cancel.Cancel();
            }
        });

        // If the encoder fails it stops taking pictures, and the readers would wait for room for ever.
        Task read = Task.WhenAll(video, sound);
        if (await Task.WhenAny(read, writer.Encoding!) != read)
        {
            cancel.Cancel();
            await read;
        }
        try
        {
            await writer.FinishAsync();
        }
        catch (Exception ex) when (failure == null && ex is not OutOfMemoryException)
        {
            failure = ex;
        }

        if (failure != null)
        {
            TryDelete(target);
            string reason = failure is COMException com ? $"0x{com.HResult:X8}" : failure.Message;
            throw new InvalidOperationException($"Windows no pudo aplicar el zoom al video ({reason}).", failure);
        }
        progress?.Report(1);
    }

    private static double SecondsOf(int bytes) => bytes / (double)(Mp4Writer.AudioChannels * 2 * Mp4Writer.AudioSampleRate);

    /// <summary>
    /// Fills the sound from <paramref name="from"/> to <paramref name="to"/> with silence, so a piece without sound is quiet
    /// for as long as it plays. Returns where the sound now ends.
    /// </summary>
    private static async Task<double> AddSilenceAsync(Mp4Writer writer, double from, double to, CancellationToken cancel)
    {
        const double Chunk = 0.02;
        const int BytesPerFrame = Mp4Writer.AudioChannels * 2;
        while (to - from > 0.001)
        {
            int frames = (int)Math.Round(Math.Min(Chunk, to - from) * Mp4Writer.AudioSampleRate);
            if (frames <= 0)
            {
                break;
            }
            await writer.AddAudioAsync(new byte[frames * BytesPerFrame], TimeSpan.FromSeconds(from), cancel);
            from += frames / (double)Mp4Writer.AudioSampleRate;
        }
        return Math.Max(from, to);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
