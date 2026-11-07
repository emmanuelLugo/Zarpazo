using Zarpazo.Pin;

namespace Zarpazo.Tests;

public sealed class PinnedWindowTests
{
    [Fact]
    public void The_wheel_up_makes_it_more_solid_and_down_more_transparent()
    {
        Assert.Equal(0.9, PinnedWindow.NextOpacity(0.8, 120));
        Assert.Equal(0.7, PinnedWindow.NextOpacity(0.8, -120));
    }

    [Fact]
    public void Opacity_stays_between_a_findable_minimum_and_fully_solid()
    {
        Assert.Equal(1.0, PinnedWindow.NextOpacity(1.0, 120));
        Assert.Equal(0.2, PinnedWindow.NextOpacity(0.2, -120));
    }

    [Fact]
    public void Ten_steps_down_from_solid_do_not_drift_off_the_tenths()
    {
        double opacity = 1.0;
        for (int i = 0; i < 4; i++)
        {
            opacity = PinnedWindow.NextOpacity(opacity, -120);
        }

        Assert.Equal(0.6, opacity);
    }
}
