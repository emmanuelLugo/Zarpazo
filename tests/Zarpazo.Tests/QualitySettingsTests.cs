using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Zarpazo.Output;
using Zarpazo.Recording;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

[Collection("Recording")]
public sealed class QualitySettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public QualitySettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string FilePath => Path.Combine(_folder, "settings.json");

    // ---------- the settings

    [Fact]
    public void The_defaults_are_what_the_app_always_did()
    {
        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(new RecordingQuality(12, false, 30, 30, VideoQuality.Medium, 30), settings.RecordingQuality);
        Assert.Equal(92, settings.JpegQuality);
    }

    [Fact]
    public void The_choices_are_read_back_by_the_next_run()
    {
        var first = new SettingsStore(FilePath);
        first.Current.RecordingQuality = new RecordingQuality(20, true, 60, 24, VideoQuality.High, 10);
        first.Current.JpegQuality = 75;
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;

        Assert.Equal(new RecordingQuality(20, true, 60, 24, VideoQuality.High, 10), second.RecordingQuality);
        Assert.Equal(75, second.JpegQuality);
    }

    [Fact]
    public void Values_that_are_not_on_offer_snap_to_the_nearest_one_and_unknown_quality_means_medium()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """
            { "GifFramesPerSecond": 100, "GifMaxSeconds": 1, "VideoFramesPerSecond": 100, "VideoMaxMinutes": 1000,
              "VideoQuality": "ultra", "JpegQuality": 1 }
            """);

        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(new RecordingQuality(20, false, 15, 60, VideoQuality.Medium, 60), settings.RecordingQuality);
        Assert.Equal(60, settings.JpegQuality);
    }

    [Fact]
    public void The_jobs_take_their_limits_from_the_quality()
    {
        var quality = new RecordingQuality(15, false, 60, 24, VideoQuality.Low, 10);

        Assert.Equal(TimeSpan.FromSeconds(60), new GifJob(new Rectangle(0, 0, 100, 100), null, quality).MaxDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), new VideoJob(new Rectangle(0, 0, 100, 100), RecordingOptions.Default, 0, quality).MaxDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), new GifJob(new Rectangle(0, 0, 100, 100)).MaxDuration); // no quality given: as before
    }

    [Fact]
    public void A_better_video_quality_spends_more_bits_on_every_pixel()
    {
        double low = new RecordingQuality(VideoQuality: VideoQuality.Low).BitsPerPixelPerFrame;
        double medium = new RecordingQuality(VideoQuality: VideoQuality.Medium).BitsPerPixelPerFrame;
        double high = new RecordingQuality(VideoQuality: VideoQuality.High).BitsPerPixelPerFrame;

        Assert.True(low < medium && medium < high);
        Assert.Equal(0.12, medium); // the value used before there was a choice
    }

    // ---------- they reach the result

    private static byte[] Noise(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }
        return pixels;
    }

    // Random 12 x 12 blocks of four grays, new every picture: it can be squeezed more or less, so the bitrate asked for shows in the size.
    // (Pure noise is no use: it can't be squeezed below a floor, whatever is asked for.)
    private static byte[] Blocks(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];
        for (int by = 0; by < height; by += 12)
        {
            for (int bx = 0; bx < width; bx += 12)
            {
                byte gray = (byte)(random.Next(4) * 80);
                for (int y = by; y < Math.Min(height, by + 12); y++)
                {
                    for (int x = bx; x < Math.Min(width, bx + 12); x++)
                    {
                        int i = (y * width + x) * 4;
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
                        pixels[i + 3] = 255;
                    }
                }
            }
        }
        return pixels;
    }

    private async Task<long> VideoSizeAsync(string name, VideoQuality quality)
    {
        string path = Path.Combine(_folder, name);
        var writer = new Mp4Writer(path, 960, 540, fps: 30, withAudio: false, new RecordingQuality(VideoQuality: quality).BitsPerPixelPerFrame);
        writer.Start();
        for (int frame = 0; frame < 60; frame++)
        {
            writer.AddVideo(Blocks(960, 540, frame), TimeSpan.FromSeconds(frame / 30.0));
            await Task.Delay(33);
        }
        await writer.FinishAsync();
        return new FileInfo(path).Length;
    }

    [HardwareEncoderFact]
    public async Task A_higher_quality_makes_a_bigger_video_of_the_same_picture()
    {
        long low = await VideoSizeAsync("low.mp4", VideoQuality.Low);
        long medium = await VideoSizeAsync("medium.mp4", VideoQuality.Medium);
        long high = await VideoSizeAsync("high.mp4", VideoQuality.High);

        Assert.True(low < medium && medium < high, $"sizes should grow with quality: {low} < {medium} < {high}");
        Assert.True(high > low * 1.3, $"high {high} should be well above low {low}");
    }

    [Fact]
    public async Task The_frame_rate_chosen_is_the_one_the_video_has()
    {
        var job = new VideoJob(new Rectangle(0, 0, 320, 180), new RecordingOptions(RecordingFormat.Video, false, false), 0,
            new RecordingQuality(VideoFramesPerSecond: 15));
        job.Start(_folder);
        await Task.Delay(1200);

        string? path = await job.FinishAsync();

        MediaClip clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path!));
        var rate = clip.GetVideoEncodingProperties().FrameRate; // given as a fraction of a very small unit of time
        Assert.InRange((double)rate.Numerator / rate.Denominator, 12, 18); // measured from the pictures taken, so not exact; the default would be 30
    }

    [Fact]
    public void The_gif_recorder_paces_itself_by_the_chosen_rate()
    {
        var recorder = new ScreenRecorder(new Rectangle(0, 0, 100, 100), TimeSpan.FromSeconds(5), 1_000_000, null, framesPerSecond: 20);

        Assert.Equal(20, recorder.FramesPerSecond);
        Assert.Equal(ScreenRecorder.DefaultFramesPerSecond, new ScreenRecorder(new Rectangle(0, 0, 100, 100), TimeSpan.FromSeconds(5), 1_000_000).FramesPerSecond);
    }

    [Fact]
    public void A_higher_jpg_quality_makes_a_bigger_file()
    {
        BitmapSource photo = BitmapSource.Create(200, 200, 96, 96, PixelFormats.Bgra32, null, Noise(200, 200, 3), 200 * 4);

        string small = Path.Combine(_folder, "q60.jpg");
        string big = Path.Combine(_folder, "q100.jpg");
        ImageOutput.Write(small, photo, 60);
        ImageOutput.Write(big, photo, 100);

        Assert.True(new FileInfo(big).Length > new FileInfo(small).Length * 1.5);
    }

    [Fact]
    public void A_png_ignores_the_jpg_quality()
    {
        BitmapSource picture = BitmapSource.Create(50, 50, 96, 96, PixelFormats.Bgra32, null, Noise(50, 50, 4), 50 * 4);

        ImageOutput.Write(Path.Combine(_folder, "a.png"), picture, 60);
        ImageOutput.Write(Path.Combine(_folder, "b.png"), picture, 100);

        Assert.Equal(new FileInfo(Path.Combine(_folder, "a.png")).Length, new FileInfo(Path.Combine(_folder, "b.png")).Length);
    }

    // ---------- dithering

    // A smooth horizontal ramp of gray, which a handful of colors can only show as steps.
    private static RecordedFrame Ramp(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte gray = (byte)(x * 255 / (width - 1));
                int i = (y * width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
                pixels[i + 3] = 255;
            }
        }
        return new RecordedFrame(0, 0, width, height, pixels, 0);
    }

    private static byte[] DecodeGray(RecordedFrame frame, bool dither, int maxColors)
    {
        using var gif = new MemoryStream();
        GifEncoder.Encode([frame], frame.Width, frame.Height, 100, gif, dither, maxColors);
        gif.Position = 0;

        byte[] result = null!;
        Sta.Run(() =>
        {
            BitmapSource image = new FormatConvertedBitmap(
                BitmapDecoder.Create(gif, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
            result = new byte[frame.Width * frame.Height * 4];
            image.CopyPixels(result, frame.Width * 4, 0);
        });
        return result;
    }

    // How far, on average, the average of each 8 x 8 block is from what it should be: what the eye sees from a little way back.
    private static double BlockError(byte[] decoded, RecordedFrame original)
    {
        double total = 0;
        int blocks = 0;
        for (int by = 0; by + 8 <= original.Height; by += 8)
        {
            for (int bx = 0; bx + 8 <= original.Width; bx += 8)
            {
                double want = 0;
                double got = 0;
                for (int y = by; y < by + 8; y++)
                {
                    for (int x = bx; x < bx + 8; x++)
                    {
                        int i = (y * original.Width + x) * 4;
                        want += original.Pixels[i];
                        got += decoded[i];
                    }
                }
                total += Math.Abs(want - got) / 64;
                blocks++;
            }
        }
        return total / blocks;
    }

    [Fact]
    public void Dithering_keeps_the_look_of_a_gradient_that_plain_colors_turn_into_steps()
    {
        RecordedFrame ramp = Ramp(128, 32);

        double plain = BlockError(DecodeGray(ramp, dither: false, maxColors: 6), ramp);
        double dithered = BlockError(DecodeGray(ramp, dither: true, maxColors: 6), ramp);

        Assert.True(dithered < plain * 0.5, $"dithered {dithered:F2} should be far closer than plain {plain:F2}");
    }

    [Fact]
    public void Dithering_does_not_change_flat_colors()
    {
        var pixels = new byte[32 * 16 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 80;
            pixels[i + 2] = 200;
            pixels[i + 3] = 255;
        }
        var frame = new RecordedFrame(0, 0, 32, 16, pixels, 0);

        byte[] decoded = DecodeGray(frame, dither: true, maxColors: 256);

        Assert.All(Enumerable.Range(0, 32 * 16), p => Assert.InRange(decoded[p * 4], 36, 44)); // the blue channel: no noise added
    }

    [Fact]
    public void Dithered_frames_that_only_change_part_of_the_picture_still_leave_the_rest_transparent()
    {
        var first = Ramp(64, 16);
        var secondPixels = (byte[])first.Pixels.Clone();
        for (int y = 4; y < 8; y++)
        {
            for (int x = 10; x < 20; x++)
            {
                int i = (y * 64 + x) * 4;
                secondPixels[i] = secondPixels[i + 1] = secondPixels[i + 2] = 0;
            }
        }

        using var gif = new MemoryStream();
        GifEncoder.Encode([first, new RecordedFrame(0, 0, 64, 16, secondPixels, 100)], 64, 16, 200, gif, dither: true);
        gif.Position = 0;

        Sta.Run(() =>
        {
            BitmapDecoder decoder = BitmapDecoder.Create(gif, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var pixel = new byte[4];
            new FormatConvertedBitmap(decoder.Frames[1], PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(40, 12, 1, 1), pixel, 4, 0);
            Assert.Equal(0, pixel[3]); // unchanged: see-through, so the first frame shows
        });
    }

    [Fact]
    public void The_nearest_palette_entry_is_found_for_colors_that_are_not_in_the_picture()
    {
        // A picture of only black and white.
        var pixels = new byte[4 * 4];
        pixels[3] = pixels[7] = 255;
        pixels[8] = pixels[9] = pixels[10] = 255;
        pixels[11] = pixels[15] = 255;
        pixels[12] = pixels[13] = pixels[14] = 255;
        GifPalette palette = GifPalette.Build(pixels, null, 256);

        int dark = palette.Nearest(30, 30, 30);
        int light = palette.Nearest(220, 220, 220);

        Assert.NotEqual(dark, light);
        Assert.Equal(palette.IndexOf(0, 0, 0), dark);
        Assert.Equal(palette.IndexOf(255, 255, 255), light);
    }
}
