using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>The line round the recording in its frame and the title bar above it.</summary>
public sealed class FrameBarAndBorderTests
{
    private const int Width = 640, Height = 360;

    private static readonly VideoFrameStyle Barred = VideoFrameStyle.Default with { Enabled = true, Padding = 0.1, Radius = 0.02, Shadow = 0 } with { Bar = WindowBar.Mac };

    [Fact]
    public void Without_a_bar_or_a_line_the_layout_is_what_it_always_was()
    {
        FrameLayout layout = FrameLayout.For(VideoFrameStyle.Default with { Enabled = true }, Width, Height);

        Assert.Equal(0, layout.BarHeight);
        Assert.Equal(0, layout.BorderPixels);
        Assert.Equal(layout.Content, layout.Card);
    }

    [Fact]
    public void The_bar_sits_right_above_the_recording_and_both_are_centred_in_the_room_left()
    {
        FrameLayout layout = FrameLayout.For(Barred, Width, Height);

        Assert.True(layout.BarHeight >= 8 && layout.BarHeight % 2 == 0);
        Assert.Equal(layout.Content.Y - layout.BarHeight, layout.Card.Y);
        Assert.Equal(layout.Content.Height + layout.BarHeight, layout.Card.Height);
        Assert.InRange(layout.Card.X + layout.Card.Width / 2, Width / 2 - 1, Width / 2 + 1);
        Assert.InRange(layout.Card.Y + layout.Card.Height / 2, Height / 2 - 1, Height / 2 + 1);
        Assert.True(layout.Card.Y >= (int)(Barred.Padding * Height) - 1, "the bar stays inside the room the frame leaves");
        Assert.True(layout.Card.Y + layout.Card.Height <= Height - (int)(Barred.Padding * Height) + 1);
        Assert.Equal(Width / (double)Height, (double)layout.Content.Width / layout.Content.Height, 1); // the recording keeps its shape
    }

    [Fact]
    public void The_recording_is_a_little_smaller_with_a_bar_to_leave_it_room()
    {
        FrameLayout plain = FrameLayout.For(Barred with { Bar = WindowBar.None }, Width, Height);
        FrameLayout barred = FrameLayout.For(Barred, Width, Height);

        Assert.True(barred.Content.Height < plain.Content.Height);
        Assert.True(barred.ContentScale < plain.ContentScale);
    }

    [Fact]
    public void A_point_of_the_recording_still_lands_inside_the_recording_not_the_bar()
    {
        FrameLayout layout = FrameLayout.For(Barred, Width, Height);

        (double _, double top) = layout.ToScene(0, 0);
        Assert.Equal(layout.Content.Y / (double)Height, top, 6);
    }

    [Fact]
    public void A_video_in_another_shape_has_only_the_background_even_if_the_style_has_a_bar_and_a_line()
    {
        VideoFrameStyle off = (Barred with { Enabled = false, BorderWidth = 0.004 }).ForScene(letterboxed: true);

        Assert.True(off.Enabled);
        Assert.Equal((WindowBar.None, 0.0), (off.Bar, off.BorderWidth));
    }

    [Fact]
    public void Odd_values_in_a_file_are_brought_back_inside_what_is_offered()
    {
        VideoFrameStyle odd = (VideoFrameStyle.Default with { BorderWidth = 5, BorderColor = "rojo", Bar = (WindowBar)42 }).Tidied();

        Assert.Equal(VideoFrameStyle.MaxBorder, odd.BorderWidth);
        Assert.Equal(VideoFrameStyle.DefaultBorderColor, odd.BorderColor);
        Assert.Equal(WindowBar.None, odd.Bar);
        Assert.Equal("#0A0B0C", (VideoFrameStyle.Default with { BorderColor = "#0a0b0c" }).Tidied().BorderColor);
    }

    [Fact]
    public void The_settings_file_keeps_the_line_and_the_bar()
    {
        var settings = new Zarpazo.Settings.AppSettings
        {
            VideoFrame = VideoFrameStyle.Default with { Enabled = true, BorderWidth = 0.004, BorderColor = "#112233", Bar = WindowBar.Windows, LightBar = true },
        };

        var back = System.Text.Json.JsonSerializer.Deserialize<Zarpazo.Settings.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;

        VideoFrameStyle frame = back.VideoFrame;
        Assert.Equal((0.004, "#112233", WindowBar.Windows, true), (frame.BorderWidth, frame.BorderColor, frame.Bar, frame.LightBar));
        Assert.Equal(VideoFrameStyle.Default.BorderWidth, new Zarpazo.Settings.AppSettings().VideoFrame.BorderWidth);
    }

    [Fact]
    public void The_bar_is_drawn_as_pixels_the_size_of_the_card_across_and_the_bar_down()
    {
        FrameLayout layout = FrameLayout.For(Barred, Width, Height);

        byte[]? bar = FrameBackdrop.RenderBar(Barred, layout);

        Assert.NotNull(bar);
        Assert.Equal(layout.Card.Width * layout.BarHeight * 4, bar!.Length);
        Assert.Null(FrameBackdrop.RenderBar(Barred with { Bar = WindowBar.None }, FrameLayout.For(Barred with { Bar = WindowBar.None }, Width, Height)));

        // Dark by default (the middle of the bar, away from the buttons) and light when asked.
        byte[] dark = Pixel(bar, layout.Card.Width, layout.Card.Width / 2, layout.BarHeight / 2);
        byte[]? lightBar = FrameBackdrop.RenderBar(Barred with { LightBar = true }, layout);
        byte[] light = Pixel(lightBar!, layout.Card.Width, layout.Card.Width / 2, layout.BarHeight / 2);
        Assert.True(dark[1] < 100 && light[1] > 200, $"dark {dark[1]}, light {light[1]}");

        // The three buttons of the Mac bar are red, yellow and green, from the left.
        double step = layout.BarHeight * 0.56, first = layout.BarHeight * 0.62;
        byte[] red = Pixel(bar, layout.Card.Width, (int)first, layout.BarHeight / 2);
        byte[] green = Pixel(bar, layout.Card.Width, (int)(first + 2 * step), layout.BarHeight / 2);
        Assert.True(red[2] > 200 && red[1] < 120, "the first button is red");
        Assert.True(green[1] > 150 && green[2] < 120, "the third is green");
    }

    [Fact]
    public void The_compositor_lays_the_bar_over_the_recordings_top_and_the_line_along_the_edge()
    {
        VideoFrameStyle style = Barred with { BorderWidth = 0.01, BorderColor = "#00FF00", LightBar = true };
        FrameLayout layout = FrameLayout.For(style, Width, Height);
        byte[] backdrop = Fill(Width, Height, 255, 0, 0);   // blue
        byte[] source = Fill(Width, Height, 0, 0, 255);     // red
        byte[] bar = FrameBackdrop.RenderBar(style, layout)!;
        var compositor = new FrameCompositor(layout, backdrop, Width, Height, null, bar, Color.FromRgb(0, 255, 0));
        var picture = new byte[Width * Height * 4];

        compositor.Render(source, picture, ZoomView.Full);

        int middle = layout.Card.X + layout.Card.Width / 2;
        byte[] inBar = Pixel(picture, Width, middle, layout.Content.Y - layout.BarHeight / 2);
        byte[] inRecording = Pixel(picture, Width, middle, layout.Content.Y + layout.Content.Height / 2);
        byte[] onLine = Pixel(picture, Width, layout.Card.X + 1, layout.Content.Y + layout.Content.Height / 2);
        byte[] outside = Pixel(picture, Width, 2, 2);

        Assert.True(inBar[0] > 200 && inBar[1] > 200 && inBar[2] > 200, $"the light bar is nearly white: {string.Join(",", inBar)}");
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, inRecording);
        Assert.True(onLine[1] > 200 && onLine[2] < 60, $"the line is green: {string.Join(",", onLine)}");
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, outside);

        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            foreach ((string name, VideoFrameStyle variant) in new[]
            {
                ("frame-bar-mac-dark.png", Barred with { Shadow = 0.7 }),
                ("frame-bar-windows-light.png", Barred with { Bar = WindowBar.Windows, LightBar = true, Shadow = 0.7, BorderWidth = 0.004, BorderColor = "#FFFFFF" }),
            })
            {
                FrameLayout shown = FrameLayout.For(variant, Width, Height);
                byte[] mock = Fill(Width, Height, 90, 60, 40);
                var frame = new byte[Width * Height * 4];
                new FrameCompositor(shown, FrameBackdrop.Render(variant, shown), Width, Height, null, FrameBackdrop.RenderBar(variant, shown),
                    variant.BorderWidth > 0 ? Color.FromRgb(255, 255, 255) : null).Render(mock, frame, ZoomView.Full);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr32, null, frame, Width * 4)));
                using FileStream stream = File.Create(Path.Combine(folder, name));
                encoder.Save(stream);
            }
        }
    }

    [Fact]
    public void A_zoomed_in_view_fills_the_picture_with_the_recording_and_the_bar_leaves()
    {
        FrameLayout layout = FrameLayout.For(Barred, Width, Height);
        byte[] bar = FrameBackdrop.RenderBar(Barred, layout)!;
        var compositor = new FrameCompositor(layout, Fill(Width, Height, 255, 0, 0), Width, Height, null, bar);
        var picture = new byte[Width * Height * 4];

        compositor.Render(Fill(Width, Height, 0, 0, 255), picture, ZoomView.Around(3, 0.5, 0.55));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(picture, Width, 2, 2));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(picture, Width, Width - 3, Height - 3));
    }

    [Fact]
    public void Every_look_to_start_from_still_has_values_in_range_with_its_bar_and_line()
    {
        foreach ((_, _, VideoFrameStyle style) in VideoFrameStyle.Looks)
        {
            Assert.Equal(style, style.Tidied());
        }
        Assert.Contains(VideoFrameStyle.Looks, look => look.Style.Bar != WindowBar.None);
        Assert.Contains(VideoFrameStyle.Looks, look => look.Style.BorderWidth > 0);
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
