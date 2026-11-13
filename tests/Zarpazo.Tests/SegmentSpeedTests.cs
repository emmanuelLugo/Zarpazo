using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class SegmentSpeedTests
{
    [Fact]
    public void A_piece_played_twice_as_fast_takes_half_the_time_and_has_no_sound()
    {
        var piece = new VideoSegment(2, 8, Speed: 2);

        Assert.Equal(6, piece.Length);
        Assert.Equal(3, piece.PlayLength);
        Assert.True(piece.IsRetimed);
        Assert.True(piece.IsSilent);
        Assert.Equal(6, piece.ToOriginal(2)); // 2 s into the piece is 4 s of the original
        Assert.Equal(2, piece.ToPlay(6));
    }

    [Fact]
    public void A_piece_with_no_speed_set_plays_as_recorded()
    {
        VideoSegment piece = default(VideoSegment) with { End = 4 };

        Assert.Equal(1, piece.Rate);
        Assert.Equal(4, piece.PlayLength);
        Assert.False(piece.IsSilent);
    }

    [Fact]
    public void Speeding_up_a_piece_shortens_the_video_and_moves_the_pieces_after_it()
    {
        var timeline = new EditTimeline(10);
        timeline.Split(4); // 0-4 and 4-10

        Assert.True(timeline.SetSpeed(1, 2));

        Assert.Equal(7, timeline.Duration);       // 4 + 6 / 2
        Assert.True(timeline.IsEdited);
        Assert.Equal(4, timeline.StartOf(1));
        Assert.Equal((1, 8.0), timeline.Locate(6)); // 2 s into the fast piece is 4 s into it in the original
        Assert.Equal(6, timeline.ToTimeline(1, 8));
    }

    [Fact]
    public void Slowing_down_the_whole_recording_counts_as_an_edit_and_can_be_undone()
    {
        var timeline = new EditTimeline(10);

        Assert.True(timeline.SetSpeed(0, 0.5));
        Assert.Equal(20, timeline.Duration);
        Assert.True(timeline.HasChanges);

        timeline.Undo();
        Assert.Equal(10, timeline.Duration);
        Assert.False(timeline.HasChanges);
        Assert.False(timeline.SetSpeed(0, 1)); // already at 1
    }

    [Fact]
    public void Speeds_stay_within_the_ones_offered()
    {
        var timeline = new EditTimeline(10);

        timeline.SetSpeed(0, 50);
        Assert.Equal(VideoSegment.Speeds[^1], timeline.Segments[0].Speed);

        timeline.SetSpeed(0, 0);
        Assert.Equal(VideoSegment.Speeds[0], timeline.Segments[0].Speed);
    }

    [Fact]
    public void Splitting_a_fast_piece_keeps_the_speed_on_both_halves()
    {
        var timeline = new EditTimeline(10);
        timeline.SetSpeed(0, 2); // 5 s long

        Assert.True(timeline.Split(2)); // at 4 s of the original

        Assert.Equal(new[] { new VideoSegment(0, 4, Speed: 2), new VideoSegment(4, 10, Speed: 2) }, timeline.Segments);
        Assert.Equal(5, timeline.Duration);
    }

    [Fact]
    public void A_zoom_inside_a_fast_piece_lasts_as_long_as_the_piece_plays_it()
    {
        // The zoom covers 4-8 s of the original; played at 2x after a 2 s piece, it shows from 2 + 0 to 2 + 2.
        var camera = new ZoomCamera([new VideoSegment(0, 2), new VideoSegment(4, 8, Speed: 2)], [new ZoomRegion(4, 8, 2, 0.5, 0.5)]);

        ZoomSpan span = Assert.Single(camera.Spans);
        Assert.Equal(2, span.Start, 6);
        Assert.Equal(4, span.End, 6);
    }

    [Fact]
    public void The_pointer_of_a_fast_piece_comes_from_further_along_the_original()
    {
        VideoSegment[] pieces = [new VideoSegment(0, 2), new VideoSegment(4, 8, Speed: 2)];

        Assert.Equal(1, CursorLayer.ToOriginal(pieces, 1), 6);
        Assert.Equal(6, CursorLayer.ToOriginal(pieces, 3), 6); // 1 s into the fast piece
        Assert.Equal(8, CursorLayer.ToOriginal(pieces, 10), 6);
    }

    [Fact]
    public void A_fast_piece_is_narrower_on_the_timeline()
    {
        var timeline = new EditTimeline(10);
        timeline.Split(4);
        timeline.SetSpeed(1, 3); // 6 s that play in 2

        (double left, double right) = TimelineLayout.Span(timeline, 1, 10);

        Assert.Equal(20, right - left, 6);
    }

    [Theory]
    [InlineData(2, "2×")]
    [InlineData(0.5, "0,5×")]
    [InlineData(1.5, "1,5×")]
    [InlineData(0.75, "0,75×")]
    public void Speeds_are_written_the_spanish_way(double speed, string text) => Assert.Equal(text, TimeText.Speed(speed));
}
