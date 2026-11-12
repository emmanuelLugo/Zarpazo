using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class ZoomTests
{
    private static CursorTrack Clicks(params (double Seconds, int X, int Y)[] clicks) =>
        new(1000, 500, [], clicks.Select(click => new CursorClick((long)(click.Seconds * 1000), (long)(click.Seconds * 1000) + 100, click.X, click.Y, ClickButton.Left)).ToList());

    // ---- Suggestions ----

    [Fact]
    public void A_lone_click_gets_a_zoom_of_two_seconds_centred_on_it_and_looking_at_it()
    {
        ZoomRegion zoom = Assert.Single(ZoomSuggester.Suggest(Clicks((5, 250, 100)), duration: 20));

        Assert.Equal(2.0, zoom.Length, 3);
        Assert.InRange((zoom.Start + zoom.End) / 2, 4.9, 5.2);
        Assert.Equal((0.25, 0.2), (zoom.FocusX, zoom.FocusY));
        Assert.Equal(ZoomRegion.DefaultScale, zoom.Scale);
    }

    [Fact]
    public void Clicks_close_in_time_and_place_share_one_zoom_from_just_before_the_first_to_just_after_the_last()
    {
        ZoomRegion zoom = Assert.Single(ZoomSuggester.Suggest(Clicks((3, 400, 200), (4.5, 440, 220), (6, 420, 240)), duration: 20));

        Assert.Equal(2.5, zoom.Start, 3);
        Assert.Equal(6.6, zoom.End, 3); // the last button comes up at 6.1 s
        Assert.Equal((0.42, 0.44), (zoom.FocusX, zoom.FocusY));
    }

    [Fact]
    public void A_long_pause_between_clicks_makes_two_zooms()
    {
        Assert.Equal(2, ZoomSuggester.Suggest(Clicks((2, 400, 200), (8, 400, 200)), duration: 20).Count);
    }

    [Fact]
    public void Clicks_too_far_apart_to_fit_in_one_zoomed_view_get_their_own_zooms_without_overlapping()
    {
        IReadOnlyList<ZoomRegion> zooms = ZoomSuggester.Suggest(Clicks((3, 50, 50), (4, 950, 450)), duration: 20);

        Assert.Equal(2, zooms.Count);
        Assert.True(zooms[0].End <= zooms[1].Start);
    }

    [Fact]
    public void Clicks_outside_the_picture_or_after_the_end_are_ignored_and_zooms_stay_inside_the_recording()
    {
        Assert.Empty(ZoomSuggester.Suggest(Clicks((3, -20, 50), (4, 50, 600), (30, 50, 50)), duration: 20));

        ZoomRegion early = Assert.Single(ZoomSuggester.Suggest(Clicks((0.1, 100, 100)), duration: 20));
        Assert.Equal(0, early.Start);
        Assert.Equal(2.0, early.Length, 3);
    }

    // ---- The camera ----

    [Fact]
    public void The_camera_zooms_in_holds_and_zooms_back_out_within_the_zoom()
    {
        var camera = new ZoomCamera([new VideoSegment(0, 20)], [new ZoomRegion(5, 10, 2, 0.5, 0.5)]);

        Assert.True(camera.ViewAt(4.9).IsFull);
        Assert.InRange(camera.ViewAt(5.3).Scale, 1.3, 1.95); // on its way in
        Assert.Equal(2, camera.ViewAt(7.5).Scale, 2);
        Assert.InRange(camera.ViewAt(9.5).Scale, 1.0, 1.9); // on its way out
        Assert.True(camera.ViewAt(10.1).IsFull);
    }

    [Fact]
    public void A_point_near_the_edge_is_looked_at_without_showing_anything_outside_the_picture()
    {
        var camera = new ZoomCamera([new VideoSegment(0, 20)], [new ZoomRegion(5, 10, 2, 0.98, 0.02)]);

        ZoomView view = camera.ViewAt(7.5);

        Assert.Equal(0.5, view.Width, 3);
        Assert.Equal(0.5, view.X, 3);  // as far right as it goes
        Assert.Equal(0.0, view.Y, 3);  // as far up as it goes
        for (double t = 5; t < 10; t += 0.05)
        {
            ZoomView moving = camera.ViewAt(t);
            Assert.InRange(moving.X, 0, 1 - moving.Width + 1e-9);
            Assert.InRange(moving.Y, 0, 1 - moving.Height + 1e-9);
        }
    }

    [Fact]
    public void Zooms_close_together_stay_zoomed_in_and_the_camera_glides_between_them()
    {
        var camera = new ZoomCamera([new VideoSegment(0, 30)],
            [new ZoomRegion(5, 9, 2, 0.25, 0.5), new ZoomRegion(10, 14, 2, 0.75, 0.5)]);

        for (double t = 7; t < 12; t += 0.1)
        {
            Assert.InRange(camera.ViewAt(t).Scale, 1.99, 2.01);
        }
        Assert.Equal(0.0, camera.ViewAt(8).X, 2);   // left half
        Assert.Equal(0.5, camera.ViewAt(11).X, 2);  // right half
    }

    [Fact]
    public void A_zoom_follows_its_moment_when_pieces_are_cut_or_moved()
    {
        // The zoom is at 10-12 s of the original; the first 8 s are cut, so it plays at 2-4 s of the result.
        var camera = new ZoomCamera([new VideoSegment(8, 20)], [new ZoomRegion(10, 12, 2, 0.5, 0.5)]);

        Assert.Equal(new ZoomSpan(2, 4, 2, 0.5, 0.5, 0), Assert.Single(camera.Spans));

        // Cut away entirely: no zoom left.
        Assert.True(new ZoomCamera([new VideoSegment(0, 9)], [new ZoomRegion(10, 12, 2, 0.5, 0.5)]).IsEmpty);
    }

    [Fact]
    public void The_curves_start_at_zero_end_at_one_and_never_go_back()
    {
        double before = 0;
        for (double t = 0; t <= 1; t += 0.01)
        {
            double value = ZoomEasing.Out(t);
            Assert.True(value >= before - 1e-9);
            before = value;
        }
        Assert.Equal((0.0, 1.0), (ZoomEasing.Out(0), ZoomEasing.Out(1)));
        Assert.Equal(0.5, ZoomEasing.InOut(0.5), 3);
        Assert.True(ZoomEasing.Out(0.3) > 0.7, "zooming in is quick at first");
    }

    // ---- The row of zooms ----

    [Fact]
    public void The_row_tells_the_middle_of_a_zoom_from_its_sides()
    {
        // At 10 pixels a second a zoom from 2 s to 6 s goes from x = 34 to x = 74 (after the padding of 14).
        ZoomSpan[] spans = [new ZoomSpan(2, 6, 2, 0.5, 0.5, 0)];

        Assert.Equal((ZoomLaneView.Part.StartEdge, 0), ZoomLaneView.HitTest(spans, 10, new System.Windows.Point(35, 15)));
        Assert.Equal((ZoomLaneView.Part.Body, 0), ZoomLaneView.HitTest(spans, 10, new System.Windows.Point(54, 15)));
        Assert.Equal((ZoomLaneView.Part.EndEdge, 0), ZoomLaneView.HitTest(spans, 10, new System.Windows.Point(73, 15)));
        Assert.Equal((ZoomLaneView.Part.None, -1), ZoomLaneView.HitTest(spans, 10, new System.Windows.Point(100, 15)));
        Assert.Equal((ZoomLaneView.Part.None, -1), ZoomLaneView.HitTest(spans, 10, new System.Windows.Point(54, 80)));
    }

    // ---- Editing ----

    [Fact]
    public void The_zooms_an_edit_starts_with_are_not_a_change_to_undo()
    {
        var timeline = new EditTimeline(20);

        timeline.StartWithZooms([new ZoomRegion(2, 4, 2, 0.5, 0.5)]);

        Assert.Single(timeline.Zooms);
        Assert.False(timeline.CanUndo);
        Assert.False(timeline.HasChanges);

        timeline.SetZoomScale(0, 3);
        Assert.True(timeline.HasChanges);
        timeline.SetZoomScale(0, 2); // back as it opened
        Assert.False(timeline.HasChanges);
    }

    [Fact]
    public void The_pointer_position_at_a_moment_is_where_it_last_moved()
    {
        var track = new CursorTrack(200, 100, [new CursorSample(0, 20, 10), new CursorSample(1000, 100, 50)], []);

        Assert.Equal((0.1, 0.1), track.PositionAt(500));
        Assert.Equal((0.5, 0.5), track.PositionAt(1000));
        Assert.Equal((0.5, 0.5), track.PositionAt(9000));
        Assert.Null(new CursorTrack(200, 100, [], []).PositionAt(0));
    }

    [Fact]
    public void Zooms_can_be_added_moved_resized_and_deleted_and_every_change_undone()
    {
        var timeline = new EditTimeline(20);
        timeline.SetZooms([new ZoomRegion(2, 4, 2, 0.5, 0.5), new ZoomRegion(10, 12, 2, 0.5, 0.5)]);

        Assert.Equal(1, timeline.AddZoom(new ZoomRegion(5, 7, 2, 0.1, 0.1)));
        Assert.True(timeline.MoveZoom(1, 0)); // stops at the end of the zoom before
        Assert.Equal(new ZoomRegion(4, 6, 2, 0.1, 0.1), timeline.Zooms[1]);
        Assert.True(timeline.SetZoomEnd(1, 50)); // stops at the start of the zoom after
        Assert.Equal(10, timeline.Zooms[1].End);
        Assert.True(timeline.SetZoomStart(1, 9.9)); // keeps the shortest length
        Assert.Equal(10 - ZoomRegion.MinSeconds, timeline.Zooms[1].Start, 6);
        Assert.True(timeline.SetZoomScale(1, 99));
        Assert.Equal(ZoomRegion.MaxScale, timeline.Zooms[1].Scale);
        Assert.True(timeline.DeleteZoom(1));
        Assert.Equal(2, timeline.Zooms.Count);

        for (int i = 0; i < 6; i++)
        {
            timeline.Undo();
        }
        Assert.Equal(2, timeline.Zooms.Count); // back to the automatic ones
        timeline.Undo();
        Assert.Empty(timeline.Zooms);
    }

    [Fact]
    public void A_zoom_cannot_be_added_on_top_of_another_and_is_shortened_to_the_free_space()
    {
        var timeline = new EditTimeline(20);
        timeline.SetZooms([new ZoomRegion(4, 8, 2, 0.5, 0.5)]);

        Assert.Equal(-1, timeline.AddZoom(new ZoomRegion(5, 7, 2, 0.5, 0.5)));
        Assert.Equal(1, timeline.AddZoom(new ZoomRegion(7, 11, 2, 0.5, 0.5)));
        Assert.Equal(8, timeline.Zooms[1].Start);
    }

    [Fact]
    public void Cutting_the_video_keeps_the_zooms_and_undo_brings_back_both_together()
    {
        var timeline = new EditTimeline(20);
        timeline.SetZooms([new ZoomRegion(4, 8, 2, 0.5, 0.5)]);

        timeline.Split(10);
        timeline.Delete(1);
        Assert.Single(timeline.Zooms);

        timeline.Undo();
        timeline.Undo();
        Assert.Single(timeline.Segments);
        Assert.Single(timeline.Zooms);
    }
}
