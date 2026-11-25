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
    /// result shows. The camera and the pointer are then on the result's clock, and the sound of those pieces is stretched to match. Null plays
    /// the source as it is.</param>
    /// <param name="overlays">The blurs, texts and arrows to draw over every picture, under the pointer; null for none.</param>
    /// <param name="output">The size, quality and frame rate to write; null for the recording's own (see <see cref="VideoExportOptions"/>).
    /// Everything above is worked out at the picture's own size, and the result is made smaller at the end.</param>
    /// <param name="aspect">The video's shape (see <see cref="VideoAspect"/>); another than the cropped picture's puts it in the middle
    /// of a canvas of that shape, on the frame's background (with no frame chosen, only the background).</param>
    public static async Task RenderAsync(string source, string target, ZoomCamera camera, VideoFrameStyle? frame, IProgress<double>? progress = null,
        CursorLayer? cursor = null, VideoCrop crop = default, PlayClock? clock = null, OverlayLayer? overlays = null, VideoExportOptions? output = null,
        VideoAspect aspect = VideoAspect.Original, bool motionBlur = false)
    {
        output ??= VideoExportOptions.Default;
        StorageFile file = await StorageFile.GetFileFromPathAsync(source);
        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(file);
        Windows.Storage.FileProperties.VideoProperties info = await file.Properties.GetVideoPropertiesAsync();
        int sourceWidth = (int)profile.Video.Width & ~1;
        int sourceHeight = (int)profile.Video.Height & ~1;
        CropArea area = CropArea.For(crop, sourceWidth, sourceHeight);
        // The scene is the video: the cropped picture's own size, or a canvas of another shape that it sits in.
        (int width, int height) = aspect.SceneSize(area.Width, area.Height);
        frame = (frame ?? VideoFrameStyle.Default).ForScene(aspect.IsLetterboxed(area.Width, area.Height));
        // In a frame the scene grows so the recording keeps its own pixels, with the frame around it.
        FrameLayout? layout = frame.Enabled ? FrameLayout.ForExport(frame, width, height, area.Width, area.Height) : null;
        if (layout is { } grown)
        {
            (width, height) = (grown.Width, grown.Height);
        }
        double recordedRate = VideoFrameReader.RecordedFrameRate(source)
            ?? profile.Video.FrameRate.Numerator / (double)Math.Max(1u, profile.Video.FrameRate.Denominator);
        // Recordings stamp their pictures in whole milliseconds (gaps of 33 and 34 ms make 29.4), so the rate is taken as the
        // nearest usual one.
        // Never fewer than 30: the camera moves every picture, so even over a recording with few pictures it glides.
        int recordedFps = Math.Max(MinFramesPerSecond, StandardRates.MinBy(rate => Math.Abs(rate - recordedRate)));
        // A higher rate than the recording's is not made up (the same pictures twice would only weigh more).
        int fps = output.Fps > 0 ? Math.Min(output.Fps, recordedFps) : recordedFps;
        (int outWidth, int outHeight) = output.SizeFor(width, height);
        bool shrunk = outWidth != width || outHeight != height;
        bool withAudio = profile.Audio != null;
        double duration = Math.Max(0.1, clock?.Duration ?? info.Duration.TotalSeconds);
        // At least the quality of the original, and never less than about 9 Mbps at 1080p: while the camera moves every pixel
        // changes, and at the 2 Mbps a mostly still screen recording gets, the zoom came out soft and blocky.
        double bitsPerPixel = Math.Clamp(profile.Video.Bitrate / ((double)sourceWidth * sourceHeight * recordedFps), MinBitsPerPixel, 0.5) * output.QualityFactor;

        FrameCompositor? compositor = null;
        if (layout != null)
        {
            System.Windows.Media.Color? border = frame.BorderWidth > 0 && FrameBackground.TryHex(frame.BorderColor, out System.Windows.Media.Color line) ? line : null;
            compositor = new FrameCompositor(layout.Value, FrameBackdrop.Render(frame, layout.Value), sourceWidth, sourceHeight, area.Pixels,
                FrameBackdrop.RenderBar(frame, layout.Value), border);
        }

        var writer = new Mp4Writer(target, outWidth, outHeight, fps, withAudio, bitsPerPixel, live: false);
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
                // The next pictures are decoded on another thread while this one draws the current one.
                using var reader = new FrameReadAhead(source, sourceWidth, sourceHeight);
                byte[]? full = shrunk ? new byte[width * height * 4] : null;
                byte[]? sharp = motionBlur ? new byte[width * height * 4] : null; // a picture the camera moves in, before it is smeared
                if (!reader.TryTake(out byte[] current, out TimeSpan currentTime))
                {
                    return;
                }
                bool hasNext = reader.TryTake(out byte[] next, out TimeSpan nextTime);
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
                        reader.Give(current);
                        current = next;
                        currentTime = nextTime;
                        hasNext = reader.TryTake(out next, out nextTime);
                    }
                    if (!hasNext && at >= currentTime.TotalSeconds + frame / 2)
                    {
                        break; // the last recorded picture has had its time on screen
                    }

                    byte[] picture = writer.RentPicture();
                    byte[] canvas = full ?? picture; // drawn at the picture's own size, then made smaller into the writer's
                    ZoomView view = camera.ViewAt(t);
                    ZoomView before = view, after = view; // where the camera is as the shutter opens and closes
                    int smear = 1;
                    if (sharp != null)
                    {
                        before = camera.ViewAt(t - MotionBlur.ShutterSeconds / 2);
                        after = camera.ViewAt(t + MotionBlur.ShutterSeconds / 2);
                        smear = MotionBlur.Samples(MotionBlur.Travel(before, after, width, height));
                    }
                    byte[] scene = smear > 1 ? sharp! : canvas;
                    if (compositor != null)
                    {
                        compositor.Render(current, scene, view);
                    }
                    else
                    {
                        ZoomResampler.Render(current, sourceWidth, sourceHeight, area.Pixels, scene, width, height, view);
                    }
                    overlays?.Draw(scene, width, height, t, view, layout);
                    if (smear > 1)
                    {
                        MotionBlur.Camera(scene, canvas, width, height, before, view, after, smear);
                    }
                    (ZoomView, ZoomView)? pointerShutter = sharp == null ? null
                        : (camera.ViewAt(t - MotionBlur.PointerShutterSeconds / 2), camera.ViewAt(t + MotionBlur.PointerShutterSeconds / 2));
                    cursor?.Draw(canvas, width, height, t, view, layout, pointerShutter);
                    if (full != null)
                    {
                        PictureShrinker.Shrink(full, width, height, picture, outWidth, outHeight);
                    }
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
                if (clock == null)
                {
                    while (reader.Read(out TimeSpan time) is { } pcm)
                    {
                        await writer.AddAudioAsync(pcm, TimeSpan.FromSeconds(Math.Max(0, time.TotalSeconds)), cancel.Token);
                    }
                    return;
                }
                await WriteRetimedSoundAsync(reader, clock, new SoundLine(writer, cancel.Token));
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

    /// <summary>
    /// Writes the sound of the joined file on the finished video's clock: each piece at its own place, the faster or slower ones
    /// stretched to their new length without changing their pitch, and quiet where a piece is muted.
    /// </summary>
    private static async Task WriteRetimedSoundAsync(AudioChunkReader reader, PlayClock clock, SoundLine line)
    {
        const int BytesPerFrame = Mp4Writer.AudioChannels * 2;
        TimeStretcher? stretcher = null;
        int stretching = -1;
        long stretched = 0; // samples of stretched sound written for the piece being stretched

        async Task WriteStretchedAsync(byte[] pcm)
        {
            if (pcm.Length > 0)
            {
                await line.AddAsync(pcm, clock.PlayStart(stretching) + stretched / (double)Mp4Writer.AudioSampleRate);
                stretched += pcm.Length / BytesPerFrame;
            }
        }

        async Task EndStretchAsync()
        {
            if (stretcher != null)
            {
                await WriteStretchedAsync(stretcher.Flush());
                stretcher = null;
                stretching = -1;
            }
        }

        while (reader.Read(out TimeSpan time) is { } pcm)
        {
            double at = Math.Max(0, time.TotalSeconds);
            int frames = pcm.Length / BytesPerFrame;
            // A piece of sound can run over the end of one piece into the next: it is split where they meet.
            for (int offset = 0; offset < frames;)
            {
                double now = at + offset / (double)Mp4Writer.AudioSampleRate;
                int index = clock.PieceAt(now);
                int count = frames - offset;
                if (index < clock.Count - 1)
                {
                    count = Math.Min(count, Math.Max(1, (int)Math.Round((clock.InputEnd(index) - now) * Mp4Writer.AudioSampleRate)));
                }
                byte[] part = count == frames ? pcm : pcm[(offset * BytesPerFrame)..((offset + count) * BytesPerFrame)];
                offset += count;

                if (stretching != index)
                {
                    await EndStretchAsync();
                }
                VideoSegment piece = clock.Piece(index);
                if (piece.Muted)
                {
                    continue; // quiet: filled in when the next sound is written
                }
                if (piece.IsRetimed)
                {
                    if (stretcher == null)
                    {
                        stretcher = new TimeStretcher(piece.Rate);
                        stretching = index;
                        stretched = 0;
                    }
                    await WriteStretchedAsync(stretcher.Process(part));
                }
                else
                {
                    await line.AddAsync(part, clock.ToPlay(now));
                }
            }
        }
        await EndStretchAsync();
        await line.FillToAsync(clock.Duration);
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
