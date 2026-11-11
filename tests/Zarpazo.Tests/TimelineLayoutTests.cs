using System.Windows;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class TimelineLayoutTests
{
    private const double Scale = 50; // pixels per second

    // Pieces of 3, 3 and 4 seconds: x from 14 to 164, 164 to 314 and 314 to 514.
    private static EditTimeline Three()
    {
        var timeline = new EditTimeline(10);
        timeline.Split(3);
        timeline.Split(6);
        return timeline;
    }

    private static TimelineHit Hit(double x, double y) => TimelineLayout.HitTest(Three(), Scale, new Point(x, y));

    private const double GripY = TimelineLayout.TrackTop + 5;
    private const double BodyY = TimelineLayout.TrackTop + 50;

    [Fact]
    public void Every_piece_has_its_place_along_the_timeline()
    {
        EditTimeline timeline = Three();

        Assert.Equal((14.0, 164.0), TimelineLayout.Span(timeline, 0, Scale));
        Assert.Equal((164.0, 314.0), TimelineLayout.Span(timeline, 1, Scale));
        Assert.Equal((314.0, 514.0), TimelineLayout.Span(timeline, 2, Scale));
        Assert.Equal(10 * Scale + 28, TimelineLayout.Width(timeline, Scale));
        Assert.Equal(3.0, TimelineLayout.TimeAt(TimelineLayout.XOf(3, Scale), Scale), 6);
    }

    [Fact]
    public void The_ruler_the_title_strip_and_the_pictures_are_told_apart()
    {
        Assert.Equal(new TimelineHit(TimelinePart.Ruler, -1), Hit(100, 10));
        Assert.Equal(new TimelineHit(TimelinePart.Grip, 0), Hit(100, GripY));
        Assert.Equal(new TimelineHit(TimelinePart.Body, 1), Hit(240, BodyY));
        Assert.Equal(new TimelineHit(TimelinePart.Body, 2), Hit(400, BodyY));
    }

    [Fact]
    public void The_sides_of_a_piece_are_edges_that_can_be_grabbed()
    {
        Assert.Equal(new TimelineHit(TimelinePart.StartEdge, 0), Hit(16, BodyY));
        Assert.Equal(new TimelineHit(TimelinePart.EndEdge, 0), Hit(162, BodyY));
        Assert.Equal(new TimelineHit(TimelinePart.StartEdge, 1), Hit(164, BodyY)); // where two pieces meet, the one that starts
        Assert.Equal(new TimelineHit(TimelinePart.EndEdge, 0), Hit(160, GripY));    // the edges reach the title strip too
    }

    [Fact]
    public void The_end_of_the_last_piece_can_be_grabbed_from_just_outside_it()
    {
        Assert.Equal(new TimelineHit(TimelinePart.EndEdge, 2), Hit(517, BodyY));
        Assert.Equal(TimelinePart.None, Hit(530, BodyY).Part);
    }

    [Fact]
    public void Empty_space_is_nothing()
    {
        Assert.Equal(TimelinePart.None, Hit(5, BodyY).Part);
        Assert.Equal(TimelinePart.None, Hit(240, TimelineLayout.TrackTop + TimelineLayout.TrackHeight + 4).Part);
        Assert.Equal(TimelinePart.None, Hit(240, -3).Part);
    }

    [Fact]
    public void A_very_short_piece_keeps_room_in_the_middle()
    {
        var timeline = new EditTimeline(10);
        timeline.Split(0.5); // 0.5 s at 20 px per second is 10 px wide: edges of 3 px each, the middle is left for the title strip
        var middle = TimelineLayout.HitTest(timeline, 20, new Point(14 + 5, TimelineLayout.TrackTop + 5));

        Assert.Equal(new TimelineHit(TimelinePart.Grip, 0), middle);
    }

    [Theory]
    [InlineData(0, 0)]    // far to the left of everything
    [InlineData(150, 0)]  // still before the middle of the next piece
    [InlineData(250, 1)]  // past the middle of the second piece (239)
    [InlineData(500, 2)]  // past the middle of the last one (414)
    public void A_dragged_piece_lands_after_the_pieces_whose_middle_it_has_passed(double centerX, int expected)
    {
        Assert.Equal(expected, TimelineLayout.DropIndex(Three(), 0, Scale, centerX));
    }

    [Fact]
    public void The_line_that_shows_where_a_piece_lands_sits_in_the_gap()
    {
        EditTimeline timeline = Three();

        Assert.Equal(164, TimelineLayout.DropLineX(timeline, 0, Scale, 0)); // before the second piece, now first
        Assert.Equal(314, TimelineLayout.DropLineX(timeline, 0, Scale, 1));
        Assert.Equal(514, TimelineLayout.DropLineX(timeline, 0, Scale, 2));
        Assert.Equal(TimelineLayout.Padding, TimelineLayout.DropLineX(new EditTimeline(10), 0, Scale, 0)); // nothing else to land among
    }

    [Theory]
    [InlineData(200, 0.5)]
    [InlineData(100, 1)]
    [InlineData(20, 5)]
    [InlineData(1, 120)]
    [InlineData(0.0001, 3600)]
    public void The_marks_of_the_ruler_leave_room_for_their_labels(double pixelsPerSecond, double expectedStep)
    {
        double step = TimelineLayout.RulerStep(pixelsPerSecond);

        Assert.Equal(expectedStep, step);
        Assert.True(step * pixelsPerSecond >= 80 || step == 3600);
    }

    [Theory]
    [InlineData(3.4, "0:03.4")]
    [InlineData(65, "1:05.0")]
    [InlineData(3723.5, "1:02:03.5")]
    [InlineData(-2, "0:00.0")]
    public void Times_read_as_minutes_and_seconds(double seconds, string expected)
    {
        Assert.Equal(expected, TimeText.Format(seconds));
    }

    [Fact]
    public void Ruler_marks_show_tenths_only_when_they_are_closer_than_a_second()
    {
        Assert.Equal("0:05", TimeText.Mark(5, 5));
        Assert.Equal("0:00.5", TimeText.Mark(0.5, 0.5));
    }
}
