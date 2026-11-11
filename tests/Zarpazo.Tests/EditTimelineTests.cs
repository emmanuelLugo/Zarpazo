using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class EditTimelineTests
{
    private static EditTimeline Ten() => new(10);

    private static double[] Starts(EditTimeline timeline) => timeline.Segments.Select(s => s.Start).ToArray();

    [Fact]
    public void It_starts_as_the_whole_recording_in_one_piece()
    {
        EditTimeline timeline = Ten();

        Assert.Equal(new[] { new VideoSegment(0, 10) }, timeline.Segments);
        Assert.Equal(10, timeline.Duration);
        Assert.False(timeline.IsEdited);
        Assert.False(timeline.CanUndo);
    }

    [Fact]
    public void Splitting_cuts_the_piece_in_two_without_losing_anything()
    {
        EditTimeline timeline = Ten();

        Assert.True(timeline.Split(4));

        Assert.Equal(new[] { new VideoSegment(0, 4), new VideoSegment(4, 10) }, timeline.Segments);
        Assert.Equal(10, timeline.Duration);
    }

    [Theory]
    [InlineData(0.1)]  // too close to the start
    [InlineData(9.9)]  // too close to the end
    [InlineData(0)]
    [InlineData(10)]
    public void A_cut_that_would_leave_a_sliver_is_refused(double at)
    {
        EditTimeline timeline = Ten();

        Assert.False(timeline.Split(at));
        Assert.Single(timeline.Segments);
        Assert.False(timeline.CanUndo);
    }

    [Fact]
    public void Deleting_a_piece_in_the_middle_joins_its_neighbours()
    {
        EditTimeline timeline = Ten();
        timeline.Split(3);
        timeline.Split(6);

        Assert.True(timeline.Delete(1));

        Assert.Equal(new[] { new VideoSegment(0, 3), new VideoSegment(6, 10) }, timeline.Segments);
        Assert.Equal(7, timeline.Duration);
    }

    [Fact]
    public void The_last_piece_cannot_be_deleted()
    {
        EditTimeline timeline = Ten();

        Assert.False(timeline.Delete(0));
        Assert.Equal(10, timeline.Duration);
    }

    [Fact]
    public void Pieces_can_be_moved_to_another_place_in_the_order()
    {
        EditTimeline timeline = Ten();
        timeline.Split(3);
        timeline.Split(6);

        Assert.True(timeline.Move(2, 0));
        Assert.Equal(new double[] { 6, 0, 3 }, Starts(timeline));

        Assert.True(timeline.Move(0, 1));
        Assert.Equal(new double[] { 0, 6, 3 }, Starts(timeline));
        Assert.False(timeline.Move(1, 1));
        Assert.False(timeline.Move(0, 7));
    }

    [Fact]
    public void A_piece_can_be_repeated_right_after_itself()
    {
        EditTimeline timeline = Ten();
        timeline.Split(4);

        Assert.True(timeline.Duplicate(0));

        Assert.Equal(new[] { new VideoSegment(0, 4), new VideoSegment(0, 4), new VideoSegment(4, 10) }, timeline.Segments);
        Assert.Equal(14, timeline.Duration);
    }

    [Fact]
    public void The_edges_of_a_piece_can_go_back_into_what_was_cut_away()
    {
        EditTimeline timeline = Ten();
        timeline.Split(3);
        timeline.Split(6);
        timeline.Delete(1); // 3 to 6 is gone

        timeline.TrimEnd(0, 5);   // the first piece grows into the deleted part
        timeline.TrimStart(1, 5); // and so does the next one: they now overlap, which is allowed

        Assert.Equal(new[] { new VideoSegment(0, 5), new VideoSegment(5, 10) }, timeline.Segments);
    }

    [Fact]
    public void The_edges_stay_inside_the_recording_and_leave_the_piece_a_little_long()
    {
        EditTimeline timeline = Ten();
        timeline.Split(5);

        timeline.TrimStart(0, -4);
        timeline.TrimEnd(1, 99);
        Assert.Equal(new[] { new VideoSegment(0, 5), new VideoSegment(5, 10) }, timeline.Segments);

        timeline.TrimStart(0, 5);   // would empty the piece
        timeline.TrimEnd(1, 0);
        Assert.Equal(5 - EditTimeline.MinSeconds, timeline.Segments[0].Start, 6);
        Assert.Equal(5 + EditTimeline.MinSeconds, timeline.Segments[1].End, 6);
    }

    [Fact]
    public void A_moment_of_the_edit_is_found_in_the_right_piece_of_the_original()
    {
        EditTimeline timeline = Ten();
        timeline.Split(3);
        timeline.Split(6);
        timeline.Delete(1);        // plays 0-3 then 6-10
        timeline.Move(1, 0);       // now 6-10 then 0-3

        Assert.Equal((0, 6.0), timeline.Locate(0));
        Assert.Equal((0, 8.5), timeline.Locate(2.5));
        Assert.Equal((1, 0.0), timeline.Locate(4));   // the boundary belongs to the piece that starts
        Assert.Equal((1, 2.0), timeline.Locate(6));
        Assert.Equal((1, 3.0), timeline.Locate(7));   // the very end
        Assert.Equal((1, 3.0), timeline.Locate(99));
        Assert.Equal((0, 6.0), timeline.Locate(-5));
    }

    [Fact]
    public void Positions_convert_both_ways()
    {
        EditTimeline timeline = Ten();
        timeline.Split(3);
        timeline.Delete(0); // plays 3-10

        Assert.Equal(0, timeline.StartOf(0));
        Assert.Equal(2, timeline.ToTimeline(0, 5));
        Assert.Equal(0, timeline.ToTimeline(0, 1), 6); // before the piece: clamped
        Assert.Equal(7, timeline.ToTimeline(0, 50), 6);
    }

    [Fact]
    public void Every_change_can_be_undone_and_redone()
    {
        EditTimeline timeline = Ten();
        timeline.Split(3);
        timeline.Split(6);
        timeline.Delete(1);

        timeline.Undo();
        Assert.Equal(3, timeline.Segments.Count);
        timeline.Undo();
        Assert.Single(timeline.Segments.Where(s => s.End == 10));
        Assert.Equal(2, timeline.Segments.Count);
        timeline.Redo();
        Assert.Equal(3, timeline.Segments.Count);

        timeline.Split(8); // a new change drops what was left to redo
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void A_drag_that_changes_a_piece_many_times_is_one_step_to_undo()
    {
        EditTimeline timeline = Ten();
        timeline.Split(5);

        timeline.BeginGesture();
        timeline.TrimEnd(0, 4.5);
        timeline.TrimEnd(0, 4);
        timeline.TrimEnd(0, 3);
        timeline.EndGesture();

        Assert.Equal(3, timeline.Segments[0].End);
        timeline.Undo();
        Assert.Equal(5, timeline.Segments[0].End);
        timeline.Undo();
        Assert.Single(timeline.Segments); // the next undo is the split, not another step of the drag
    }

    [Fact]
    public void A_piece_can_be_muted_and_it_counts_as_an_edit()
    {
        EditTimeline timeline = Ten();

        Assert.True(timeline.SetMuted(0, true));
        Assert.True(timeline.Segments[0].Muted);
        Assert.True(timeline.IsEdited);
        Assert.False(timeline.SetMuted(0, true), "nothing changes, so nothing to undo");

        timeline.Undo();
        Assert.False(timeline.Segments[0].Muted);
    }

    [Fact]
    public void A_muted_piece_keeps_being_muted_when_it_is_split()
    {
        EditTimeline timeline = Ten();
        timeline.SetMuted(0, true);

        timeline.Split(5);

        Assert.All(timeline.Segments, segment => Assert.True(segment.Muted));
    }

    [Fact]
    public void Reset_goes_back_to_the_whole_recording_and_can_be_undone()
    {
        EditTimeline timeline = Ten();
        timeline.Split(5);
        timeline.Delete(0);

        Assert.True(timeline.Reset());
        Assert.Equal(new[] { new VideoSegment(0, 10) }, timeline.Segments);
        Assert.False(timeline.Reset(), "already whole");

        timeline.Undo();
        Assert.Equal(new[] { new VideoSegment(5, 10) }, timeline.Segments);
    }

    [Fact]
    public void Changed_is_raised_for_every_change_and_for_undo()
    {
        EditTimeline timeline = Ten();
        int raised = 0;
        timeline.Changed += (_, _) => raised++;

        timeline.Split(5);
        timeline.Split(50); // refused: no event
        timeline.Undo();
        timeline.Redo();

        Assert.Equal(3, raised);
    }
}
