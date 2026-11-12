using System.IO;
using System.Runtime.InteropServices;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Zarpazo.Recording;

/// <summary>
/// Writes a video again with the zooms applied: every picture is decoded, the part the camera shows is stretched to the full
/// size, and the result is encoded with the same size, frame rate, quality and sound as the original.
/// </summary>
internal static class ZoomRenderer
{
    /// <param name="camera">What each moment of <paramref name="source"/> shows; its times are the source's.</param>
    /// <param name="progress">Reports 0 to 1 while it works.</param>
    /// <exception cref="InvalidOperationException">Windows could not read or write the video.</exception>
    public static async Task RenderAsync(string source, string target, ZoomCamera camera, IProgress<double>? progress = null)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(source);
        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(file);
        Windows.Storage.FileProperties.VideoProperties info = await file.Properties.GetVideoPropertiesAsync();
        int width = (int)profile.Video.Width & ~1;
        int height = (int)profile.Video.Height & ~1;
        int fps = Math.Max(1, (int)Math.Round(profile.Video.FrameRate.Numerator / (double)Math.Max(1u, profile.Video.FrameRate.Denominator)));
        bool withAudio = profile.Audio != null;
        double duration = Math.Max(0.1, info.Duration.TotalSeconds);
        // The same quality as the original: its bitrate spread over its pixels and pictures.
        double bitsPerPixel = Math.Clamp(profile.Video.Bitrate / ((double)width * height * fps), 0.05, 0.5);

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
                using var reader = new VideoFrameReader(source, width, height);
                var decoded = new byte[width * height * 4];
                while (reader.TryRead(decoded, out TimeSpan time))
                {
                    byte[] picture = writer.RentPicture();
                    ZoomResampler.Render(decoded, picture, width, height, camera.ViewAt(time.TotalSeconds));
                    await writer.AddVideoAsync(picture, time, cancel.Token);
                    progress?.Report(Math.Clamp(time.TotalSeconds / duration, 0, 1));
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
