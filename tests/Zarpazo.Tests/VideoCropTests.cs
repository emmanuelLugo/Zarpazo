using System.Windows;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Cropping a video: the model, its pixels, the change of coordinates and dragging its sides.</summary>
public sealed class VideoCropTests
{
    [Fact]
    public void A_side_stops_where_too_little_of_the_picture_would_be_left()
    {
        VideoCrop crop = VideoCrop.None.With(CropEdges.Right, 0.5).With(CropEdges.Left, 0.8);

        Assert.Equal(0.5, crop.Right);
        Assert.Equal(1 - VideoCrop.MinKept - 0.5, crop.Left, 9);
        Assert.Equal(VideoCrop.MinKept, crop.Width, 9);
        Assert.Equal(0, VideoCrop.None.With(CropEdges.Top, -0.3).Top);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(double.PositiveInfinity, -1, 2, 0.5)]
    [InlineData(0.7, 0.7, 0.7, 0.7)]
    [InlineData(0.95, 0, 0.95, 0)]
    public void Odd_values_are_tidied_into_a_crop_that_keeps_enough(double left, double top, double right, double bottom)
    {
        VideoCrop crop = new VideoCrop(left, top, right, bottom).Tidied();

        foreach (double side in new[] { crop.Left, crop.Top, crop.Right, crop.Bottom })
        {
            Assert.True(double.IsFinite(side) && side >= 0 && side <= 1, $"side {side}");
        }
        Assert.True(crop.Width >= VideoCrop.MinKept - 1e-9);
        Assert.True(crop.Height >= VideoCrop.MinKept - 1e-9);
        Assert.Equal(crop, crop.Tidied());
    }

    [Fact]
    public void The_rectangle_kept_has_an_even_size_inside_the_picture()
    {
        CropArea area = CropArea.For(new VideoCrop(0.1, 0.05, 0.133, 0.0371), 1920, 1080);

        Assert.Equal(0, area.Width % 2);
        Assert.Equal(0, area.Height % 2);
        Assert.Equal(192, area.Pixels.X);
        Assert.Equal(54, area.Pixels.Y);
        Assert.InRange(area.Pixels.X + area.Width, 1660, 1920);
        Assert.InRange(area.Pixels.Y + area.Height, 1000, 1080);
        Assert.False(area.IsWhole);
        Assert.True(CropArea.For(VideoCrop.None, 1920, 1080).IsWhole);
        Assert.Equal(new Int32Rect(0, 0, 1920, 1080), CropArea.For(VideoCrop.None, 1920, 1080).Pixels);
    }

    [Fact]
    public void A_point_of_the_recording_lands_where_it_shows_in_the_cropped_picture_and_back()
    {
        // The taskbar (the bottom 40 of 1080) and 200 px on the left cut away.
        CropArea area = CropArea.For(new VideoCrop(200 / 1920.0, 0, 0, 40 / 1080.0), 1920, 1080);

        (double x, double y) = area.ToCrop(1060 / 1920.0, 520 / 1080.0);
        Assert.Equal(860.0 / 1720, x, 9);
        Assert.Equal(520.0 / 1040, y, 9);
        (double backX, double backY) = area.FromCrop(x, y);
        Assert.Equal(1060 / 1920.0, backX, 9);
        Assert.Equal(520 / 1080.0, backY, 9);

        (double tx, double ty) = area.ToCrop(0.5, 1060 / 1080.0); // on the taskbar
        Assert.False(CropArea.Shows(tx, ty));
        Assert.True(CropArea.Shows(x, y));
    }

    [Fact]
    public void Zooms_keep_the_original_s_points_and_look_at_the_same_thing_in_the_crop()
    {
        CropArea area = CropArea.For(new VideoCrop(0.25, 0, 0.25, 0), 400, 200);
        ZoomRegion[] zooms = [new ZoomRegion(0, 2, 2, 0.5, 0.5), new ZoomRegion(3, 4, 2, 0.1, 0.5)];

        IReadOnlyList<ZoomRegion> cropped = area.ToCrop(zooms);

        Assert.Equal(0.5, cropped[0].FocusX, 9);
        Assert.Equal(0, cropped[1].FocusX); // it looked at a part cut away: the nearest edge
        Assert.Equal(0.5, zooms[0].FocusX); // the zooms themselves are left as they were
        Assert.Same(zooms, CropArea.For(VideoCrop.None, 400, 200).ToCrop(zooms));
    }

    [Fact]
    public void A_press_grabs_the_side_near_it_a_corner_or_the_middle()
    {
        var crop = new VideoCrop(0.1, 0.1, 0.1, 0.1);
        const double Reach = 0.02;

        Assert.Equal(CropEdges.Left, CropDrag.HitTest(crop, 0.11, 0.5, Reach, Reach));
        Assert.Equal(CropEdges.Bottom, CropDrag.HitTest(crop, 0.5, 0.89, Reach, Reach));
        Assert.Equal(CropEdges.Top | CropEdges.Right, CropDrag.HitTest(crop, 0.9, 0.1, Reach, Reach));
        Assert.Equal(CropEdges.All, CropDrag.HitTest(crop, 0.5, 0.5, Reach, Reach));
        Assert.Equal(CropEdges.None, CropDrag.HitTest(crop, 0.02, 0.5, Reach, Reach)); // in the part cut away
    }

    [Fact]
    public void Dragging_a_side_moves_only_it_and_moving_the_whole_stops_at_the_picture_s_edges()
    {
        var start = new VideoCrop(0.1, 0.1, 0.1, 0.1);

        VideoCrop wider = CropDrag.Drag(start, CropEdges.Right, 0.05, 0.3);
        Assert.Equal(new VideoCrop(0.1, 0.1, 0.05, 0.1), wider with { Right = Math.Round(wider.Right, 9) });

        VideoCrop moved = CropDrag.Drag(start, CropEdges.All, 0.5, -0.04);
        Assert.Equal(0.2, moved.Left, 9);
        Assert.Equal(0, moved.Right, 9);
        Assert.Equal(0.06, moved.Top, 9);
        Assert.Equal(0.8, moved.Width, 9); // its size kept

        VideoCrop squeezed = CropDrag.Drag(start, CropEdges.Left | CropEdges.Top, 2, 2);
        Assert.Equal(VideoCrop.MinKept, squeezed.Width, 9);
        Assert.Equal(VideoCrop.MinKept, squeezed.Height, 9);
    }

    [Fact]
    public void The_crop_is_undone_and_redone_with_the_rest_of_the_edit_and_a_drag_is_one_step()
    {
        var timeline = new EditTimeline(10);
        timeline.Split(5);

        timeline.BeginGesture();
        for (int i = 1; i <= 10; i++)
        {
            timeline.SetCrop(VideoCrop.None with { Bottom = i / 100.0 });
        }
        timeline.EndGesture();
        Assert.Equal(0.1, timeline.Crop.Bottom, 9);
        Assert.True(timeline.HasChanges);

        timeline.Undo();
        Assert.Equal(VideoCrop.None, timeline.Crop);
        Assert.Equal(2, timeline.Segments.Count); // the split before it stays

        timeline.Redo();
        Assert.Equal(0.1, timeline.Crop.Bottom, 9);

        timeline.Delete(1); // other edits keep the crop
        Assert.Equal(0.1, timeline.Crop.Bottom, 9);
        Assert.False(timeline.SetCrop(timeline.Crop)); // the same crop is no change
    }

    [Fact]
    public void A_crop_alone_counts_as_a_change_to_ask_about_on_closing()
    {
        var timeline = new EditTimeline(10);
        Assert.False(timeline.HasChanges);

        timeline.SetCrop(new VideoCrop(0, 0, 0, 0.04));

        Assert.True(timeline.HasChanges);
        Assert.False(timeline.IsEdited); // the pieces are still the whole recording
    }
}
