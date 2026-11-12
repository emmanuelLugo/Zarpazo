using System.IO;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>The frame a video can be shown in: a background, room around the recording, rounded corners and a shadow.</summary>
public sealed class VideoFrameTests
{
    private static readonly VideoFrameStyle Framed = VideoFrameStyle.Default with { Enabled = true, Padding = 0.1, Radius = 0.05 };

    [Fact]
    public void The_recording_sits_in_the_middle_with_room_around_it_and_keeps_its_proportions()
    {
        FrameLayout layout = FrameLayout.For(Framed, 1920, 1080);

        Assert.Equal((1920, 1080), (layout.Width, layout.Height));
        Assert.Equal(216, 1080 - layout.Content.Height); // a tenth of the height on each side
        Assert.Equal(1920.0 / 1080, (double)layout.Content.Width / layout.Content.Height, 2);
        Assert.Equal((960, 540), (layout.Content.X + layout.Content.Width / 2, layout.Content.Y + layout.Content.Height / 2));
        Assert.Equal(54, layout.RadiusPixels);
    }

    [Fact]
    public void A_point_of_the_recording_moves_into_the_scene()
    {
        FrameLayout layout = FrameLayout.For(Framed, 1920, 1080);

        Assert.Equal((0.5, 0.5), layout.ToScene(0.5, 0.5));
        (double x, double y) = layout.ToScene(0, 0);
        Assert.Equal(layout.Content.X / 1920.0, x, 6);
        Assert.Equal(layout.Content.Y / 1080.0, y, 6);
        Assert.Equal(0.5, layout.ToScene([new ZoomRegion(1, 2, 2, 0.5, 0.5)])[0].FocusX, 6);
    }

    [Fact]
    public void The_recording_shows_inside_the_frame_the_backdrop_around_it_and_a_zoom_fills_the_picture_with_it()
    {
        const int width = 200, height = 100;
        FrameLayout layout = FrameLayout.For(Framed, width, height);
        byte[] backdrop = Fill(width, height, 0, 0, 255);   // red
        byte[] source = Fill(width, height, 255, 255, 255); // white
        var compositor = new FrameCompositor(layout, backdrop, width, height);
        var picture = new byte[width * height * 4];

        compositor.Render(source, picture, ZoomView.Full);
        Assert.Equal((byte)255, Pixel(picture, width, 100, 50)[1]); // the middle: the recording
        Assert.Equal((byte)0, Pixel(picture, width, 2, 2)[1]);      // the corner: the backdrop
        Assert.Equal((byte)0, Pixel(picture, width, layout.Content.X, layout.Content.Y)[1]); // a rounded corner cuts it off

        compositor.Render(source, picture, ZoomView.Around(2, 0.5, 0.5));
        Assert.Equal((byte)255, Pixel(picture, width, 2, 2)[1]); // zoomed in, the recording fills the picture
    }

    [Fact]
    public void The_backdrop_has_the_background_and_a_shadow_under_the_recording()
    {
        FrameLayout layout = FrameLayout.For(Framed, 640, 360);
        byte[] backdrop = FrameBackdrop.Render(Framed, layout);

        byte[] corner = Pixel(backdrop, 640, 3, 3);
        byte[] underShadow = Pixel(backdrop, 640, 320, layout.Content.Y + layout.Content.Height + 6);
        Assert.True(corner[0] > 150, "the default background is violet: plenty of blue");
        Assert.True(underShadow[0] < corner[0], "the shadow darkens the background just below the recording");

        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            byte[] source = Fill(640, 360, 230, 230, 230);
            var picture = new byte[640 * 360 * 4];
            new FrameCompositor(layout, backdrop, 640, 360).Render(source, picture, ZoomView.Full);
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(640, 360, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, picture, 640 * 4)));
            using FileStream stream = File.Create(Path.Combine(folder, "frame.png"));
            encoder.Save(stream);
        }
    }

    private static byte[] Fill(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
        }
        return pixels;
    }

    private static byte[] Pixel(byte[] picture, int width, int x, int y) => picture.AsSpan((y * width + x) * 4, 4).ToArray();
}
