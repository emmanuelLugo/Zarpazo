using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class CursorLayerTests
{
    private const int Width = 400;
    private const int Height = 200;

    private static readonly CursorStyle Plain = CursorStyle.Default with { Smoothing = 0, Sway = 0, Bounce = false, Size = 2 };

    // The pointer resting at (x, y) of the recording, with a click at one second.
    private static CursorTrack StillAt(int x, int y) =>
        new(Width, Height, [new CursorSample(0, x, y)], [new CursorClick(1000, 1000, x, y, ClickButton.Left)], PointerInPicture: false);

    private static byte[] Grey()
    {
        var picture = new byte[Width * Height * 4];
        for (int i = 0; i < picture.Length; i += 4)
        {
            picture[i] = picture[i + 1] = picture[i + 2] = 0x80;
            picture[i + 3] = 255;
        }
        return picture;
    }

    private static bool Changed(byte[] picture, int x, int y)
    {
        int i = (y * Width + x) * 4;
        return picture[i] != 0x80 || picture[i + 1] != 0x80 || picture[i + 2] != 0x80;
    }

    // The box round everything drawn over the grey.
    private static Int32Rect Drawn(byte[] picture)
    {
        int left = Width, top = Height, right = -1, bottom = -1;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Changed(picture, x, y))
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }
        return right < 0 ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    [Fact]
    public void The_arrow_is_drawn_with_its_tip_where_the_pointer_was()
    {
        CursorLayer layer = CursorLayer.For(StillAt(100, 50), Plain, [new VideoSegment(0, 5)])!;
        byte[] picture = Grey();

        layer.Draw(picture, Width, Height, 0.5, ZoomView.Full, layout: null);

        Int32Rect box = Drawn(picture);
        Assert.InRange(box.X, 95, 100);   // the tip, less its edge
        Assert.InRange(box.Y, 45, 50);
        Assert.InRange(box.Height, 38, 50); // about 20 units tall at twice the size, with edge and shadow
    }

    [Fact]
    public void Zoomed_in_the_pointer_is_drawn_bigger_where_the_camera_shows_it()
    {
        CursorLayer layer = CursorLayer.For(StillAt(100, 50), Plain, [new VideoSegment(0, 5)])!;
        byte[] near = Grey();
        byte[] far = Grey();

        // Twice as close, looking at the top-left quarter: (100, 50) of the recording shows at (200, 100).
        layer.Draw(near, Width, Height, 0.5, new ZoomView(0, 0, 0.5, 0.5), layout: null);
        layer.Draw(far, Width, Height, 0.5, ZoomView.Full, layout: null);

        Int32Rect zoomed = Drawn(near);
        Assert.InRange(zoomed.X, 192, 200);
        Assert.InRange(zoomed.Y, 92, 100);
        Assert.InRange(zoomed.Height, Drawn(far).Height * 1.8, Drawn(far).Height * 2.2);
    }

    [Fact]
    public void A_click_draws_a_ring_round_the_pointer()
    {
        CursorLayer layer = CursorLayer.For(StillAt(200, 100), Plain with { Visible = true, Click = ClickEffect.Ripple }, [new VideoSegment(0, 5)])!;
        byte[] picture = Grey();

        layer.Draw(picture, Width, Height, 1.1, ZoomView.Full, layout: null);

        Int32Rect box = Drawn(picture);
        Assert.True(box.X < 190 && box.Y < 90, "the ring reaches above and left of the tip");
    }

    [Fact]
    public void There_is_nothing_to_draw_without_a_clean_track_or_with_the_pointer_switched_off()
    {
        CursorTrack painted = StillAt(1, 1) with { PointerInPicture = true };

        Assert.Null(CursorLayer.For(null, Plain, [new VideoSegment(0, 1)]));
        Assert.Null(CursorLayer.For(painted, Plain, [new VideoSegment(0, 1)]));
        Assert.Null(CursorLayer.For(StillAt(1, 1), Plain with { Visible = false }, [new VideoSegment(0, 1)]));
        Assert.NotNull(CursorLayer.For(StillAt(1, 1), Plain, [new VideoSegment(0, 1)]));
    }

    [Fact]
    public void With_a_crop_the_pointer_lands_where_it_shows_in_the_crop_and_outside_it_is_not_drawn()
    {
        // The left quarter and the bottom half cut away: 300 x 100 of the 400 x 200 recording are kept.
        CropArea area = CropArea.For(new VideoCrop(0.25, 0, 0, 0.5), Width, Height);
        byte[] inside = Grey();
        byte[] outside = Grey();

        // (250, 50) of the recording is the middle of the crop: in a picture of any size, its middle.
        CursorLayer.For(StillAt(250, 50), Plain, [new VideoSegment(0, 5)], area)!.Draw(inside, Width, Height, 0.5, ZoomView.Full, layout: null);
        CursorLayer.For(StillAt(50, 150), Plain, [new VideoSegment(0, 5)], area)!.Draw(outside, Width, Height, 1.1, ZoomView.Full, layout: null);

        Int32Rect box = Drawn(inside);
        Assert.InRange(box.X, 195, 200);
        Assert.InRange(box.Y, 95, 100);
        Assert.Equal(Int32Rect.Empty, Drawn(outside)); // neither the pointer nor its click
    }

    [Fact]
    public void A_moment_of_the_edited_video_is_found_in_the_original_through_its_pieces()
    {
        VideoSegment[] pieces = [new(4, 6), new(1, 2)];

        Assert.Equal(4.5, CursorLayer.ToOriginal(pieces, 0.5), 6);
        Assert.Equal(1.25, CursorLayer.ToOriginal(pieces, 2.25), 6);
        Assert.Equal(2, CursorLayer.ToOriginal(pieces, 10), 6);
    }

    [Fact]
    public void In_a_frame_the_pointer_follows_the_recording_into_it()
    {
        CursorLayer layer = CursorLayer.For(StillAt(0, 0), Plain, [new VideoSegment(0, 5)])!;
        FrameLayout layout = FrameLayout.For(VideoFrameStyle.Default with { Enabled = true, Padding = 0.1 }, Width, Height);
        byte[] picture = Grey();

        layer.Draw(picture, Width, Height, 0.5, ZoomView.Full, layout);

        Int32Rect box = Drawn(picture);
        Assert.InRange(box.X, layout.Content.X - 4, layout.Content.X + 1);
        Assert.InRange(box.Y, layout.Content.Y - 4, layout.Content.Y + 1);
    }

    // Draws every shape and click effect to a PNG when ZARPAZO_SCREENSHOTS names a folder, to judge them by eye.
    [Fact]
    public void Every_style_can_be_drawn()
    {
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        const int cell = 100;
        var sheet = new byte[Width * Height * 4];
        for (int i = 0; i < sheet.Length; i += 4)
        {
            // The top half like a dark window, the bottom like a light one.
            byte value = i / 4 / Width < Height / 2 ? (byte)0x26 : (byte)0xF2;
            sheet[i] = sheet[i + 1] = sheet[i + 2] = value;
            sheet[i + 3] = 255;
        }
        (CursorStyle Style, double Seconds)[] cells =
        [
            (Plain with { Shape = CursorShape.DarkArrow }, 0.5),
            (Plain with { Shape = CursorShape.LightArrow }, 0.5),
            (Plain with { Shape = CursorShape.Dot }, 0.5),
            (Plain with { Click = ClickEffect.Ripple }, 1.12),
        ];
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < cells.Length; column++)
            {
                int x = column * cell + 40, y = row * cell + 30;
                (CursorStyle style, double seconds) = cells[column];
                if (row == 1 && column == 3)
                {
                    style = style with { Click = ClickEffect.Pulse };
                }
                CursorLayer.For(StillAt(x, y), style, [new VideoSegment(0, 5)])!.Draw(sheet, Width, Height, seconds, ZoomView.Full, null);
            }
        }

        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        BitmapSource bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, sheet, Width * 4);
        var scaled = new TransformedBitmap(bitmap, new ScaleTransform(2, 2));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        Directory.CreateDirectory(folder);
        using FileStream stream = File.Create(Path.Combine(folder, "cursor-styles.png"));
        encoder.Save(stream);
    }
}
