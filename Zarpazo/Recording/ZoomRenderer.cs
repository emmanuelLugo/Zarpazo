using System.IO;
using System.Runtime.InteropServices;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Zarpazo.Recording;

/// <summary>
/// Writes a video again with the zooms applied: every picture is decoded, the part the camera shows is stretched to the full
/// size, and the result is encoded with the same size, frame rate and sound as the original.
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
    public static async Task RenderAsync(string source, string target, ZoomCamera camera, VideoFrameStyle? frame, IProgress<double>? progress = null,
        CursorLayer? cursor = null)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(source);
        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(file);
        Windows.Storage.FileProperties.VideoProperties info = await file.Properties.GetVideoPropertiesAsync();
        int width = (int)profile.Video.Width & ~1;
        int height = (int)profile.Video.Height & ~1;
        double recordedRate = VideoFrameReader.RecordedFrameRate(source)
            ?? profile.Video.FrameRate.Numerator / (double)Math.Max(1u, profile.Video.FrameRate.Denominator);
        // Recordings stamp their pictures in whole milliseconds (gaps of 33 and 34 ms make 29.4), so the rate is taken as the
        // nearest usual one.
        // Never fewer than 30: the camera moves every picture, so even over a recording with few pictures it glides.
        int fps = Math.Max(MinFramesPerSecond, StandardRates.MinBy(rate => Math.Abs(rate - recordedRate)));
        bool withAudio = profile.Audio != null;
        double duration = Math.Max(0.1, info.Duration.TotalSeconds);
        // At least the quality of the original, and never less than about 9 Mbps at 1080p: while the camera moves every pixel
        // changes, and at the 2 Mbps a mostly still screen recording gets, the zoom came out soft and blocky.
        double bitsPerPixel = Math.Clamp(profile.Video.Bitrate / ((double)width * height * fps), MinBitsPerPixel, 0.5);

        FrameCompositor? compositor = null;
        FrameLayout? layout = null;
        if (frame is { Enabled: true })
        {
            layout = FrameLayout.For(frame, width, height);
            compositor = new FrameCompositor(layout.Value, FrameBackdrop.Render(frame, layout.Value), width, height);
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
                using var reader = new VideoFrameReader(source, width, height);
                var current = new byte[width * height * 4];
                var next = new byte[width * height * 4];
                if (!reader.TryRead(current, out TimeSpan currentTime))
                {
                    return;
                }
                bool hasNext = reader.TryRead(next, out TimeSpan nextTime);
                double frame = 1.0 / fps;
                for (long n = 0; ; n++)
                {
                    double t = n * frame;
                    while (hasNext && nextTime.TotalSeconds <= t + frame / 2)
                    {
                        (current, next) = (next, current);
                        currentTime = nextTime;
                        hasNext = reader.TryRead(next, out nextTime);
                    }
                    if (!hasNext && t >= currentTime.TotalSeconds + frame / 2)
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
                        ZoomResampler.Render(current, picture, width, height, view);
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
                while (reader.Read(out TimeSpan time) is { } pcm)
                {
                    await writer.AddAudioAsync(pcm, time < TimeSpan.Zero ? TimeSpan.Zero : time, cancel.Token);
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
