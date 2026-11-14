using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Blurs, texts and arrows over the video: the model, undoing them, where they fall on the edited video and how they are drawn.</summary>
public sealed class VideoOverlayTests
{
    private const int Width = 400;
    private const int Height = 200;

    private static VideoOverlay Blur(double start = 1, double end = 3, double x = 0.25, double y = 0.25, double x2 = 0.75, double y2 = 0.75) =>
        new(OverlayKind.Blur, start, end, x, y, x2, y2);

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

    // Left half white, right half black: a hard edge in the middle for a blur to soften.
    private static byte[] Edge()
    {
        byte[] picture = Grey();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 4;
                picture[i] = picture[i + 1] = picture[i + 2] = x < Width / 2 ? (byte)255 : (byte)0;
            }
        }
        return picture;
    }

    // Fine stripes, a column black and a column white: any blur turns them grey.
    private static byte[] Stripes()
    {
        byte[] picture = Grey();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 4;
                picture[i] = picture[i + 1] = picture[i + 2] = x % 2 == 0 ? (byte)0 : (byte)255;
            }
        }
        return picture;
    }

    private static int Green(byte[] picture, int x, int y) => picture[(y * Width + x) * 4 + 1];

    private static bool Changed(byte[] picture, int x, int y, byte from = 0x80) =>
        picture[(y * Width + x) * 4] != from || picture[(y * Width + x) * 4 + 1] != from || picture[(y * Width + x) * 4 + 2] != from;

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

    // ---- the model

    [Fact]
    public void A_blur_has_its_corners_in_order_and_inside_the_picture()
    {
        VideoOverlay tidy = Blur(x: 0.9, y: 0.8, x2: 0.1, y2: 1.4).Tidied();

        Assert.Equal((0.1, 0.8, 0.9, 1.0), (tidy.X, tidy.Y, tidy.X2, tidy.Y2));
    }

    [Fact]
    public void A_text_is_cut_to_its_longest_length_and_a_blur_has_none()
    {
        var text = new VideoOverlay(OverlayKind.Text, 0, 2, 0.1, 0.1, Text: new string('a', 500));

        Assert.Equal(VideoOverlay.MaxTextLength, text.Tidied().Text.Length);
        Assert.Equal("", (Blur() with { Text = "hola" }).Tidied().Text);
    }

    [Fact]
    public void A_blur_shows_whole_while_it_lasts_and_a_text_fades_in_and_out()
    {
        VideoOverlay blur = Blur(start: 1, end: 3);
        var text = new VideoOverlay(OverlayKind.Text, 1, 3, 0.1, 0.1, Text: "hola");

        Assert.Equal(1, blur.OpacityAt(1));
        Assert.Equal(0, blur.OpacityAt(0.99));
        Assert.Equal(0, blur.OpacityAt(3));
        Assert.Equal(0, text.OpacityAt(1));
        Assert.InRange(text.OpacityAt(1.1), 0.4, 0.6);
        Assert.Equal(1, text.OpacityAt(2));
        Assert.InRange(text.OpacityAt(2.9), 0.4, 0.6);
    }

    [Fact]
    public void Moving_an_overlay_keeps_it_inside_the_picture()
    {
        VideoOverlay moved = Blur(x: 0.2, y: 0.2, x2: 0.4, y2: 0.4).Moved(2, -2);

        Assert.Equal((0.8, 0.0, 1.0, 0.2), (Math.Round(moved.X, 6), Math.Round(moved.Y, 6), Math.Round(moved.X2, 6), Math.Round(moved.Y2, 6)));
    }

    // ---- in the edit

    [Fact]
    public void An_overlay_is_added_kept_inside_the_recording_and_can_be_undone()
    {
        var timeline = new EditTimeline(10);

        int index = timeline.AddOverlay(Blur(start: 8, end: 14));

        Assert.Equal(0, index);
        Assert.Equal((8.0, 10.0), (timeline.Overlays[0].Start, timeline.Overlays[0].End));
        Assert.True(timeline.HasChanges);

        timeline.Undo();
        Assert.Empty(timeline.Overlays);
        timeline.Redo();
        Assert.Single(timeline.Overlays);
    }

    [Fact]
    public void An_overlay_that_does_not_fit_in_the_recording_is_refused()
    {
        var timeline = new EditTimeline(10);

        Assert.Equal(-1, timeline.AddOverlay(Blur(start: 9.8, end: 12)));
        Assert.Empty(timeline.Overlays);
        Assert.False(timeline.CanUndo);
    }

    [Fact]
    public void Overlays_can_overlap_each_other()
    {
        var timeline = new EditTimeline(10);

        timeline.AddOverlay(Blur(start: 1, end: 5));
        timeline.AddOverlay(Blur(start: 2, end: 4));

        Assert.Equal(2, timeline.Overlays.Count);
    }

    [Fact]
    public void An_overlay_is_moved_and_its_sides_stretched_without_leaving_the_recording_or_getting_too_short()
    {
        var timeline = new EditTimeline(10);
        timeline.AddOverlay(Blur(start: 2, end: 4));

        timeline.MoveOverlay(0, 20);
        Assert.Equal((8.0, 10.0), (timeline.Overlays[0].Start, timeline.Overlays[0].End));

        timeline.SetOverlayStart(0, 9.9);
        Assert.Equal(10 - VideoOverlay.MinSeconds, timeline.Overlays[0].Start, 6);

        timeline.SetOverlayEnd(0, 0);
        Assert.Equal(timeline.Overlays[0].Start + VideoOverlay.MinSeconds, timeline.Overlays[0].End, 6);
    }

    [Fact]
    public void A_drag_that_changes_an_overlay_many_times_is_one_step_to_undo()
    {
        var timeline = new EditTimeline(10);
        timeline.AddOverlay(Blur(start: 2, end: 4));

        timeline.BeginGesture();
        timeline.MoveOverlay(0, 3);
        timeline.MoveOverlay(0, 4);
        timeline.MoveOverlay(0, 5);
        timeline.EndGesture();
        timeline.Undo();

        Assert.Equal(2, timeline.Overlays[0].Start);
    }

    [Fact]
    public void Deleting_and_changing_an_overlay_can_be_undone()
    {
        var timeline = new EditTimeline(10);
        timeline.AddOverlay(new VideoOverlay(OverlayKind.Text, 1, 3, 0.1, 0.1, Text: "hola"));

        Assert.True(timeline.SetOverlay(0, timeline.Overlays[0] with { Text = "adiós" }));
        Assert.Equal("adiós", timeline.Overlays[0].Text);
        Assert.False(timeline.SetOverlay(0, timeline.Overlays[0]));

        Assert.True(timeline.DeleteOverlay(0));
        Assert.Empty(timeline.Overlays);
        timeline.Undo();
        timeline.Undo();
        Assert.Equal("hola", timeline.Overlays[0].Text);
    }

    [Fact]
    public void A_cut_keeps_an_overlay_on_its_moment()
    {
        // 0-10 cut into two pieces, the second moved first: the overlay at 6-8 plays in the first half of the result.
        VideoSegment[] pieces = [new(5, 10), new(0, 5)];

        IReadOnlyList<OverlaySpan> spans = OverlaySpan.Place(pieces, [Blur(start: 6, end: 8)]);

        Assert.Single(spans);
        Assert.Equal((1.0, 3.0, 0), (spans[0].Start, spans[0].End, spans[0].Index));
    }

    [Fact]
    public void An_overlay_in_a_piece_that_is_repeated_shows_twice_and_in_one_that_is_cut_away_not_at_all()
    {
        VideoSegment[] twice = [new(0, 5), new(0, 5)];
        VideoSegment[] away = [new(0, 2), new(8, 10)];

        Assert.Equal(2, OverlaySpan.Place(twice, [Blur(start: 1, end: 2)]).Count);
        Assert.Empty(OverlaySpan.Place(away, [Blur(start: 3, end: 7)]));
    }

    // ---- drawing

    [Fact]
    public void A_blur_softens_what_is_inside_its_rectangle_and_leaves_the_rest_alone()
    {
        OverlayLayer layer = OverlayLayer.For([Blur(x: 0.25, y: 0.25, x2: 0.75, y2: 0.75)], [new VideoSegment(0, 5)])!;
        byte[] picture = Edge();

        layer.Draw(picture, Width, Height, 2, ZoomView.Full, layout: null);

        Assert.InRange(Green(picture, Width / 2, Height / 2), 90, 170);    // the middle of the edge, between white and black
        Assert.Equal(255, Green(picture, Width / 2 - 20, 10));             // outside the rectangle: untouched
        Assert.Equal(255, Green(picture, Width / 4 - 2, Height / 2));      // just outside its left side
        Assert.Equal(0, Green(picture, Width * 3 / 4 + 3, Height / 2));
    }

    [Fact]
    public void A_blur_is_not_drawn_outside_its_time()
    {
        OverlayLayer layer = OverlayLayer.For([Blur(start: 1, end: 3)], [new VideoSegment(0, 5)])!;
        byte[] before = Edge();
        byte[] picture = Edge();

        layer.Draw(picture, Width, Height, 0.5, ZoomView.Full, layout: null);
        layer.Draw(picture, Width, Height, 3.5, ZoomView.Full, layout: null);

        Assert.Equal(before, picture);
    }

    [Fact]
    public void A_blur_follows_the_camera_when_the_picture_is_zoomed()
    {
        // The camera shows the right half of the recording, so its middle is the result's left side: a blur over 0.4-0.6 of the
        // recording covers the first fifth of the result, from -0.2 to 0.2 of its width.
        OverlayLayer layer = OverlayLayer.For([Blur(x: 0.4, y: 0.25, x2: 0.6, y2: 0.75)], [new VideoSegment(0, 5)])!;
        byte[] picture = Stripes();

        layer.Draw(picture, Width, Height, 2, new ZoomView(0.5, 0, 0.5, 1), layout: null);

        Assert.InRange(Green(picture, 40, Height / 2), 100, 160);  // blurred: the stripes are grey
        Assert.Equal(0, Green(picture, 120, Height / 2));          // past its right side: still stripes
        Assert.Equal(0, Green(picture, 40, 20));                   // above it
    }

    [Fact]
    public void A_blur_is_laid_over_the_cropped_picture()
    {
        // A crop that keeps the right half of a 400 x 200 recording: the blur at 0.6-0.9 of the recording is at 0.2-0.8 of the result.
        var crop = CropArea.For(new VideoCrop(0.5, 0, 0, 0), Width, Height);
        OverlayLayer layer = OverlayLayer.For([Blur(x: 0.6, y: 0.1, x2: 0.9, y2: 0.9)], [new VideoSegment(0, 5)], crop)!;
        byte[] picture = Stripes();

        layer.Draw(picture, Width, Height, 2, ZoomView.Full, layout: null);

        Assert.InRange(Green(picture, Width / 2, Height / 2), 100, 160);
        Assert.Equal(0, Green(picture, 40, Height / 2)); // left of the blur
    }

    [Fact]
    public void An_arrow_is_drawn_from_its_tail_to_its_head()
    {
        var arrow = new VideoOverlay(OverlayKind.Arrow, 0, 5, 0.2, 0.5, 0.6, 0.5, Size: 0.03);
        OverlayLayer layer = OverlayLayer.For([arrow], [new VideoSegment(0, 5)])!;
        byte[] picture = Grey();

        layer.Draw(picture, Width, Height, 2.5, ZoomView.Full, layout: null);

        Int32Rect box = Drawn(picture);
        Assert.InRange(box.X, 70, 82);                 // the tail, a little round
        Assert.InRange(box.X + box.Width, 236, 246);   // the tip of the head, at 0.6 of 400
        Assert.InRange(Math.Abs(box.Y + box.Height / 2.0 - Height / 2.0), 0, 6);
        Assert.True(Changed(picture, 160, Height / 2));
    }

    [Fact]
    public void A_text_is_drawn_from_its_corner_in_its_colour()
    {
        var text = new VideoOverlay(OverlayKind.Text, 0, 5, 0.25, 0.25, Text: "HOLA", Ink: Color.FromRgb(255, 0, 0), Size: 0.1);
        OverlayLayer layer = OverlayLayer.For([text], [new VideoSegment(0, 5)])!;
        byte[] picture = Grey();

        layer.Draw(picture, Width, Height, 2.5, ZoomView.Full, layout: null);

        Int32Rect box = Drawn(picture);
        Assert.InRange(box.X, 98, 108);
        Assert.InRange(box.Y, 48, 62);
        Assert.InRange(box.Width, 30, 130);
        bool red = false;
        for (int y = box.Y; y < box.Y + box.Height && !red; y++)
        {
            for (int x = box.X; x < box.X + box.Width && !red; x++)
            {
                int i = (y * Width + x) * 4;
                red = picture[i + 2] > 230 && picture[i + 1] < 30 && picture[i] < 30;
            }
        }
        Assert.True(red);
    }

    [Fact]
    public void A_text_grows_with_the_zoom()
    {
        var text = new VideoOverlay(OverlayKind.Text, 0, 5, 0.1, 0.1, Text: "HOLA", Size: 0.08);
        OverlayLayer layer = OverlayLayer.For([text], [new VideoSegment(0, 5)])!;
        byte[] near = Grey();
        byte[] far = Grey();

        layer.Draw(far, Width, Height, 2.5, ZoomView.Full, layout: null);
        layer.Draw(near, Width, Height, 2.5, new ZoomView(0, 0, 0.5, 0.5), layout: null);

        Assert.True(Drawn(near).Width > Drawn(far).Width * 1.6);
    }

    [Fact]
    public void An_empty_text_draws_nothing()
    {
        var text = new VideoOverlay(OverlayKind.Text, 0, 5, 0.1, 0.1, Text: "   ");
        OverlayLayer layer = OverlayLayer.For([text], [new VideoSegment(0, 5)])!;
        byte[] picture = Grey();

        layer.Draw(picture, Width, Height, 2.5, ZoomView.Full, layout: null);

        Assert.Equal(Int32Rect.Empty, Drawn(picture));
    }

    [Fact]
    public void There_is_no_layer_without_overlays()
    {
        Assert.Null(OverlayLayer.For([], [new VideoSegment(0, 5)]));
        Assert.Null(OverlayLayer.For(null, [new VideoSegment(0, 5)]));
    }
}
