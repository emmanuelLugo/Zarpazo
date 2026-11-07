using Zarpazo.Pin;

namespace Zarpazo.Tests;

public sealed class PinScaleTests
{
    [Fact]
    public void The_wheel_scales_by_ten_percent_a_notch_and_comes_back_to_exactly_one()
    {
        double up = PinScale.Next(1, 120, 400, 300);
        double back = PinScale.Next(up, -120, 400, 300);

        Assert.Equal(1.1, up, 3);
        Assert.Equal(1, back); // snapped: no 0.99999 left over to blur the text
    }

    [Fact]
    public void Near_a_hundred_percent_it_snaps_to_it()
    {
        Assert.Equal(1, PinScale.Clamp(1.04, 400, 300));
        Assert.Equal(1, PinScale.Clamp(0.96, 400, 300));
        Assert.Equal(1.06, PinScale.Clamp(1.06, 400, 300));
    }

    [Fact]
    public void It_never_shrinks_below_a_findable_size_or_grows_past_four_times()
    {
        Assert.Equal(0.2, PinScale.Clamp(0.01, 400, 300), 3); // 60 px of the shorter side
        Assert.Equal(4, PinScale.Clamp(10, 400, 300));
    }

    [Fact]
    public void A_picture_already_smaller_than_the_minimum_cannot_shrink_further()
    {
        Assert.Equal(1, PinScale.Clamp(0.5, 40, 30));
    }

    [Fact]
    public void The_point_under_the_cursor_stays_under_it_after_scaling()
    {
        // Cursor at the middle of a picture that becomes 200 x 100: the corner moves so the middle stays at (500, 400).
        Assert.Equal((400, 350), PinScale.Origin(500, 400, 0.5, 0.5, 200, 100));
        // Anchored by its top-left corner (the grip): the corner doesn't move.
        Assert.Equal((100, 50), PinScale.Origin(100, 50, 0, 0, 300, 200));
    }
}
