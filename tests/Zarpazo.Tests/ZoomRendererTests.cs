using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Saving a video with its zooms drawn in: every picture decoded, zoomed and encoded again.</summary>
[Collection("Recording")]
public sealed class ZoomRendererTests : IClassFixture<SampleVideos>, IDisposable
{
    private const int Width = 320;
    private const int Height = 180;

    private readonly SampleVideos _samples;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public ZoomRendererTests(SampleVideos samples)
    {
        _samples = samples;
        Directory.CreateDirectory(_folder);
    }

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

    // Three seconds of a still picture: the top-left quarter white, the rest black. Which part a zoom shows can be told by colour.
    private async Task<string> QuarterVideoAsync()
    {
        string path = Path.Combine(_folder, "quarter.mp4");
        var writer = new Mp4Writer(path, Width, Height, fps: 30, withAudio: false, live: false);
        writer.Start();
        for (int frame = 0; frame < 90; frame++)
        {
            byte[] pixels = writer.RentPicture();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte value = x < Width / 2 && y < Height / 2 ? (byte)255 : (byte)0;
                    int i = (y * Width + x) * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                    pixels[i + 3] = 255;
                }
            }
            await writer.AddVideoAsync(pixels, TimeSpan.FromSeconds(frame / 30.0));
        }
        await writer.FinishAsync();
        return path;
    }

    private static async Task<MediaClip> OpenAsync(string path) =>
        await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));

    // The green of one pixel (in shares of the picture) of the picture at a moment, decoded by Windows' own editing engine.
    private static async Task<int> PixelAsync(string path, double seconds, double x, double y)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await OpenAsync(path));
        using IRandomAccessStream stream = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(seconds), Width, Height, VideoFramePrecision.NearestFrame);
        var decoder = BitmapDecoder.Create(stream.AsStreamForRead(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource picture = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        picture.CopyPixels(new System.Windows.Int32Rect((int)(picture.PixelWidth * x), (int)(picture.PixelHeight * y), 1, 1), pixel, 4, 0);
        return pixel[1];
    }

    private static readonly (byte B, byte G, byte R)[] StripeColours = [(40, 40, 200), (40, 180, 60), (200, 90, 30), (128, 128, 128)];

    // One second of four coloured stripes, to check colours survive decoding.
    private async Task<string> StripesVideoAsync(int width, int height)
    {
        string path = Path.Combine(_folder, $"stripes-{width}.mp4");
        var writer = new Mp4Writer(path, width, height, fps: 30, withAudio: false, live: false);
        writer.Start();
        for (int frame = 0; frame < 30; frame++)
        {
            byte[] pixels = writer.RentPicture();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    (byte b, byte g, byte r) = StripeColours[x * 4 / width];
                    int i = (y * width + x) * 4;
                    (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
                }
            }
            await writer.AddVideoAsync(pixels, TimeSpan.FromSeconds(frame / 30.0));
        }
        await writer.FinishAsync();
        return path;
    }

    // Compared with the colours recorded, not with Windows' thumbnails: those decode HD video with the SD formula and come
    // out visibly off (a red of 200, 40, 40 reads with a green of 24).
    [Theory]
    [InlineData(320, 180)]   // standard definition: BT.601, and the decoder pads the height to 192
    [InlineData(1280, 720)]  // HD: BT.709
    public async Task Pictures_decode_with_the_colours_they_were_recorded_with(int width, int height)
    {
        string path = await StripesVideoAsync(width, height);

        var picture = new byte[width * height * 4];
        using (var reader = new VideoFrameReader(path, width, height))
        {
            Assert.True(reader.TryRead(picture, out _));
        }

        for (int stripe = 0; stripe < 4; stripe++)
        {
            int i = (height / 2 * width + (stripe * 2 + 1) * width / 8) * 4; // the middle of each stripe
            (byte b, byte g, byte r) = StripeColours[stripe];
            byte[] expected = [b, g, r];
            for (int c = 0; c < 3; c++)
            {
                Assert.True(Math.Abs(picture[i + c] - expected[c]) <= 8,
                    $"stripe {stripe}, channel {c}: {picture[i + c]} decoded, {expected[c]} recorded");
            }
        }
    }

    [Fact]
    public void Resampling_the_whole_picture_copies_it_and_a_zoom_stretches_its_part()
    {
        // 4x2: the left half white, the right half black.
        byte[] source = new byte[4 * 2 * 4];
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                Array.Fill(source, (byte)255, (y * 4 + x) * 4, 4);
            }
        }
        var target = new byte[source.Length];

        ZoomResampler.Render(source, target, 4, 2, ZoomView.Full);
        Assert.Equal(source, target);

        ZoomResampler.Render(source, target, 4, 2, new ZoomView(0, 0, 0.25, 0.5)); // the first column, stretched
        Assert.Equal(255, target[(1 * 4 + 3) * 4]); // even the far corner shows the white half now
    }

    [Fact]
    public async Task A_zoom_on_the_top_left_fills_the_picture_with_it_and_the_picture_stays_the_right_way_up()
    {
        string source = await QuarterVideoAsync();
        string target = Path.Combine(_folder, "zoomed.mp4");
        var camera = new ZoomCamera([new VideoSegment(0, 3)], [new ZoomRegion(0, 3, 2, 0.25, 0.25)]);

        await ZoomRenderer.RenderAsync(source, target, camera);

        Assert.InRange(await PixelAsync(source, 1.5, 0.75, 0.75), 0, 40);    // before: the bottom right is black
        Assert.InRange(await PixelAsync(target, 1.5, 0.75, 0.75), 215, 255); // zoomed in on the white quarter
        Assert.InRange(await PixelAsync(target, 0.02, 0.75, 0.75), 0, 40);   // not yet zoomed at the very start
        MediaClip clip = await OpenAsync(target);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 2.8, 3.2);
        Assert.Equal((320u, 180u), (clip.GetVideoEncodingProperties().Width, clip.GetVideoEncodingProperties().Height));
    }

    [Fact]
    public async Task Pictures_recorded_unevenly_come_out_evenly_spaced_so_the_camera_moves_smoothly()
    {
        // Like a 1080p recording where copying the screen sometimes takes too long: gaps of 33, 50 and 66 ms.
        string source = Path.Combine(_folder, "uneven.mp4");
        var writer = new Mp4Writer(source, Width, Height, fps: 30, withAudio: false, live: false);
        writer.Start();
        double at = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            byte[] pixels = writer.RentPicture();
            Array.Fill(pixels, (byte)(frame * 4));
            await writer.AddVideoAsync(pixels, TimeSpan.FromSeconds(at));
            at += frame % 3 == 0 ? 0.050 : frame % 7 == 0 ? 0.066 : 0.0333;
        }
        await writer.FinishAsync();
        string target = Path.Combine(_folder, "even.mp4");

        await ZoomRenderer.RenderAsync(source, target, new ZoomCamera([new VideoSegment(0, at)], [new ZoomRegion(0.5, at - 0.5, 2, 0.5, 0.5)]));

        var times = new List<double>();
        using (var reader = new VideoFrameReader(target, Width, Height))
        {
            var picture = new byte[Width * Height * 4];
            while (reader.TryRead(picture, out TimeSpan time))
            {
                times.Add(time.TotalSeconds);
            }
        }
        Assert.InRange(times[^1] - times[0], at - 0.1, at + 0.05); // as long as the recording
        for (int i = 1; i < times.Count; i++)
        {
            Assert.InRange(times[i] - times[i - 1], 0.030, 0.037);
        }
    }

    [Fact]
    public async Task The_sound_is_kept_when_zooming()
    {
        string target = Path.Combine(_folder, "zoomed-sound.mp4");
        var camera = new ZoomCamera([new VideoSegment(0, 3)], [new ZoomRegion(0.5, 2.5, 1.8, 0.5, 0.5)]);

        await ZoomRenderer.RenderAsync(_samples.WithSound, target, camera);

        MediaClip clip = await OpenAsync(target);
        Assert.Single(clip.EmbeddedAudioTracks);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 2.7, 3.3);
    }

    [Fact]
    public async Task Cut_pieces_and_zooms_are_saved_together_and_the_joined_file_is_cleaned_up()
    {
        string target = Path.Combine(_folder, "both.mp4");
        VideoSegment[] pieces = [new VideoSegment(0, 1), new VideoSegment(2, 3)];

        await VideoExporter.ExportAsync(_samples.WithSound, target, pieces, [new ZoomRegion(2, 3, 2, 0.5, 0.5)], sourceDuration: 3);

        MediaClip clip = await OpenAsync(target);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.7, 2.3);
        Assert.Single(clip.EmbeddedAudioTracks);
        Assert.Equal(["both.mp4"], Directory.GetFiles(_folder, "both*").Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_video_recorded_without_the_pointer_is_saved_with_the_one_the_editor_draws()
    {
        string source = await QuarterVideoAsync();
        string target = Path.Combine(_folder, "pointer.mp4");
        // Resting in the black bottom-right quarter; the dark arrow has a white edge, so its outline shows on black.
        var track = new CursorTrack(Width, Height, [new CursorSample(0, 240, 120)], [], PointerInPicture: false);
        CursorStyle style = CursorStyle.Default with { Size = 3, Sway = 0 };

        await VideoExporter.ExportAsync(source, target, [new VideoSegment(0, 3)], [], sourceDuration: 3, frame: null, progress: null, track, style);

        var picture = new byte[Width * Height * 4];
        using (var reader = new VideoFrameReader(target, Width, Height))
        {
            Assert.True(reader.TryRead(picture, out _));
        }
        int bright = 0;
        for (int y = 120; y < 180; y++)
        {
            for (int x = 236; x < 290; x++)
            {
                bright += picture[(y * Width + x) * 4 + 1] > 180 ? 1 : 0;
            }
        }
        Assert.True(bright > 40, $"only {bright} light pixels where the pointer should be");
        Assert.Equal(0, picture[(170 * Width + 100) * 4 + 1] > 180 ? 1 : 0); // and none far from it
    }
}
