using System.IO;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>The shape of the finished video (16:9, 9:16, 1:1): the canvas sizes, where the recording sits, and the saved video.</summary>
[Collection("Recording")]
public sealed class VideoAspectTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 180;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoAspectTests() => Directory.CreateDirectory(_folder);

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

    // ---------- sizes

    [Theory]
    [InlineData(0, 1920, 1080, 1920, 1080)] // original: as it is
    [InlineData(1, 1920, 1080, 1920, 1080)] // 16:9 on a 16:9 recording: nothing to add
    [InlineData(2, 1920, 1080, 1080, 1920)] // 9:16: a full HD side, not 3414 tall
    [InlineData(3, 1920, 1080, 1920, 1920)] // 1:1 holds the recording at its own size
    [InlineData(1, 1440, 1080, 1920, 1080)] // 16:9 around a 4:3 recording keeps it at its own size
    [InlineData(2, 3840, 2160, 2160, 3840)] // a 4K recording: the cap follows its longest side
    [InlineData(2, 320, 180, 320, 568)] // small recordings are not blown up
    [InlineData(3, 1918, 1078, 1918, 1918)]
    public void The_canvas_is_the_smallest_of_that_shape_that_holds_the_recording_inside_full_hd(int aspect, int width, int height, int expectedWidth, int expectedHeight)
    {
        (int canvasWidth, int canvasHeight) = ((VideoAspect)aspect).SceneSize(width, height);

        Assert.Equal((expectedWidth, expectedHeight), (canvasWidth, canvasHeight));
        Assert.Equal(0, canvasWidth % 2);
        Assert.Equal(0, canvasHeight % 2);
    }

    [Fact]
    public void A_video_is_letterboxed_only_when_its_shape_differs_from_the_picture()
    {
        Assert.False(VideoAspect.Original.IsLetterboxed(1920, 1080));
        Assert.False(VideoAspect.Wide.IsLetterboxed(1920, 1080));
        Assert.False(VideoAspect.Wide.IsLetterboxed(1918, 1078)); // within a hair of 16:9
        Assert.True(VideoAspect.Tall.IsLetterboxed(1920, 1080));
        Assert.True(VideoAspect.Square.IsLetterboxed(1920, 1080));
        Assert.Equal("9:16", VideoAspect.Tall.Label());
        Assert.Equal(VideoAspect.Original, VideoAspects.All[0]);
    }

    // ---------- where the recording sits

    [Fact]
    public void In_a_taller_canvas_the_recording_is_centred_with_the_background_above_and_below()
    {
        var style = VideoFrameStyle.Default with { Enabled = true, Padding = 0, Radius = 0, Shadow = 0 };

        FrameLayout layout = FrameLayout.For(style, 1080, 1920, 1920, 1080);

        Assert.Equal((1080, 608), (layout.Content.Width, layout.Content.Height)); // 1920 x 1080 shrunk to the canvas' width
        Assert.Equal(0, layout.Content.X);
        Assert.Equal((1920 - 608) / 2, layout.Content.Y);
        Assert.Equal(1080 / 1920.0, layout.ContentScale, 3);
        (double x, double y) = layout.ToScene(0.5, 0.5);
        Assert.Equal(0.5, x, 3);
        Assert.Equal(0.5, y, 3); // the middle of the recording is the middle of the canvas
    }

    [Fact]
    public void A_scene_of_the_recordings_own_shape_lays_out_as_it_always_did()
    {
        FrameLayout layout = FrameLayout.For(VideoFrameStyle.Default with { Enabled = true }, 1920, 1080);

        Assert.Equal(layout, FrameLayout.For(VideoFrameStyle.Default with { Enabled = true }, 1920, 1080, 1920, 1080));
        Assert.Equal(layout.Content.Width / 1920.0, layout.ContentScale, 3);
    }

    [Fact]
    public void With_no_frame_chosen_the_background_still_shows_but_nothing_else_is_added()
    {
        VideoFrameStyle off = VideoFrameStyle.Default;

        Assert.Same(off, off.ForScene(letterboxed: false)); // a video of its own shape is saved as recorded
        VideoFrameStyle bars = off.ForScene(letterboxed: true);
        Assert.True(bars.Enabled);
        Assert.Equal((0, 0, 0), (bars.Padding, bars.Radius, bars.Shadow));
        Assert.Equal(off.Background, bars.Background);
        VideoFrameStyle chosen = off with { Enabled = true, Padding = 0.1 };
        Assert.Same(chosen, chosen.ForScene(letterboxed: true)); // a frame the person chose is kept as it is
    }

    [Fact]
    public void Copying_the_backdrop_where_the_recording_does_not_reach_gives_the_same_picture_as_looking_every_pixel_up()
    {
        // A tall scene with the recording in the middle, rounded corners and a shadow, over a gradient; the source has a bright patch.
        var style = VideoFrameStyle.Default with { Enabled = true, Padding = 0.04, Radius = 0.02, Shadow = 0.6 };
        FrameLayout layout = FrameLayout.For(style, 160, 284, 320, 180);
        byte[] backdrop = FrameBackdrop.Render(style, layout);
        var source = new byte[Width * Height * 4];
        for (int i = 0; i < source.Length; i += 4)
        {
            int pixel = i / 4;
            bool patch = pixel % Width < 100 && pixel / Width < 60;
            source[i] = source[i + 1] = source[i + 2] = patch ? (byte)240 : (byte)70;
            source[i + 3] = 255;
        }
        var compositor = new FrameCompositor(layout, backdrop, Width, Height);
        var fast = new byte[160 * 284 * 4];
        var slow = new byte[160 * 284 * 4];

        compositor.Render(source, fast, ZoomView.Full); // the whole scene in view: the shortcut
        compositor.Render(source, slow, new ZoomView(0, 0, 0.99999, 0.99999)); // a hair short of whole: every pixel looked up

        int worst = 0;
        for (int i = 0; i < fast.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(fast[i] - slow[i]));
        }
        Assert.InRange(worst, 0, 2); // the same picture, up to the hair's worth of shift
    }

    // ---------- the edit

    [Fact]
    public void The_shape_is_part_of_the_edit_it_is_undone_and_counts_as_a_change()
    {
        var timeline = new EditTimeline(10);

        Assert.False(timeline.SetAspect(VideoAspect.Original));
        Assert.True(timeline.SetAspect(VideoAspect.Tall));
        Assert.Equal(VideoAspect.Tall, timeline.Aspect);
        Assert.True(timeline.HasChanges);
        Assert.False(timeline.SetAspect((VideoAspect)42)); // not a shape

        timeline.Undo();
        Assert.Equal(VideoAspect.Original, timeline.Aspect);
        Assert.False(timeline.HasChanges);
        timeline.Redo();
        Assert.Equal(VideoAspect.Tall, timeline.Aspect);
    }

    [Fact]
    public void A_saved_edit_comes_back_in_its_shape_and_a_file_from_before_has_the_original_one()
    {
        var timeline = new EditTimeline(10);
        timeline.Load([new VideoSegment(0, 10)], [], VideoCrop.None, [], VideoLook.Default, VideoAspect.Square);
        Assert.Equal(VideoAspect.Square, timeline.Aspect);
        Assert.False(timeline.HasChanges, "it is where the edit starts");

        VideoProject? back = VideoProject.FromJson(VideoProject.Capture(timeline, VideoLook.Default).ToJson());
        Assert.Equal(VideoAspect.Square, back!.ToAspect());

        VideoProject old = VideoProject.FromJson("""{ "Version": 1, "SourceDuration": 5, "Segments": [], "Zooms": [], "Overlays": [], "Crop": { "Left": 0, "Top": 0, "Right": 0, "Bottom": 0 } }""")!;
        Assert.Equal(VideoAspect.Original, old.ToAspect());
        Assert.Equal(VideoAspect.Original, new VideoProject { Aspect = 42 }.ToAspect());
    }

    // ---------- the saved video

    // Three seconds of a still picture: the top-left quarter white, the rest black.
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

    // The green of a pixel (in shares of the picture) in the middle of a video, read at its own size.
    private static int Green(string path, int width, int height, double x, double y)
    {
        using var reader = new VideoFrameReader(path, width, height);
        var picture = new byte[width * height * 4];
        for (int i = 0; i < 45; i++)
        {
            Assert.True(reader.TryRead(picture, out _));
        }
        return picture[((int)(y * height) * width + (int)(x * width)) * 4 + 1];
    }

    private static async Task<(uint Width, uint Height)> SizeAsync(string path)
    {
        Windows.Media.MediaProperties.MediaEncodingProfile profile = await Windows.Media.MediaProperties.MediaEncodingProfile.CreateFromFileAsync(
            await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
        return (profile.Video.Width, profile.Video.Height);
    }

    [Fact]
    public async Task A_tall_video_has_the_recording_in_the_middle_and_the_background_around_it_with_no_frame_chosen()
    {
        string source = await QuarterVideoAsync();
        string target = Path.Combine(_folder, "tall.mp4");
        var camera = new ZoomCamera([new VideoSegment(0, 3)], []);

        await ZoomRenderer.RenderAsync(source, target, camera, frame: null, aspect: VideoAspect.Tall);

        (uint width, uint height) = await SizeAsync(target);
        Assert.Equal((320u, 568u), (width, height)); // 9:16
        // The recording (320 x 180) is in the middle: from 194 to 374 of 568 rows.
        Assert.InRange(Green(target, 320, 568, 0.25, 0.40), 230, 255); // inside its white quarter
        Assert.InRange(Green(target, 320, 568, 0.75, 0.55), 0, 25);    // inside its black part
        int above = Green(target, 320, 568, 0.5, 0.08);
        int below = Green(target, 320, 568, 0.5, 0.92);
        Assert.InRange(above, 60, 140); // the background (violet, the default), neither the white nor the black of the video
        Assert.InRange(below, 60, 140);
    }

    [Fact]
    public async Task Exporting_in_another_shape_draws_the_video_again_and_gives_it_the_shape_and_the_size_chosen()
    {
        string source = await QuarterVideoAsync();
        string square = Path.Combine(_folder, "square.mp4");
        string small = Path.Combine(_folder, "square-small.mp4");

        await VideoExporter.ExportAsync(source, square, [new VideoSegment(0, 3)], [], 3, frame: null, aspect: VideoAspect.Square);
        await VideoExporter.ExportAsync(source, small, [new VideoSegment(0, 3)], [], 3, frame: null, aspect: VideoAspect.Square,
            output: new VideoExportOptions(ExportResolution.Low, ExportQuality.Balanced, 0));

        Assert.Equal((320u, 320u), await SizeAsync(square));
        Assert.Equal((160u, 160u), await SizeAsync(small)); // the size chosen applies to the canvas
    }

    [Fact]
    public async Task A_shape_the_recording_already_has_changes_nothing()
    {
        string source = await QuarterVideoAsync();
        string target = Path.Combine(_folder, "same.mp4");

        await VideoExporter.ExportAsync(source, target, [new VideoSegment(0, 3)], [], 3, frame: null, aspect: VideoAspect.Wide);

        Assert.Equal((320u, 180u), await SizeAsync(target));
    }
}
