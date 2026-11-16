using Zarpazo.Recording;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;

namespace Zarpazo.Tests;

public class WindowFitTests
{
    private static readonly Size Designed = new(1320, 880);
    private static readonly Size Smallest = new(1040, 700);

    [Fact]
    public void A_second_monitor_at_100_percent_gets_the_designed_size_in_its_own_pixels()
    {
        // To the right of a 2560 x 1600 laptop at 150 %: that scaling must not leak into the size.
        var work = new Rectangle(2560, 0, 1920, 1040);

        (Rectangle bounds, bool maximize) = WindowFit.Centered(work, 1, Designed, Smallest);

        Assert.Equal(new Rectangle(2560 + 300, 80, 1320, 880), bounds);
        Assert.False(maximize);
    }

    [Fact]
    public void A_scaled_screen_gets_the_designed_size_times_its_scaling()
    {
        var work = new Rectangle(0, 0, 2560, 1540);

        (Rectangle bounds, bool maximize) = WindowFit.Centered(work, 1.5, Designed, Smallest);

        Assert.Equal(new Rectangle(290, 110, 1980, 1320), bounds);
        Assert.False(maximize);
    }

    [Fact]
    public void A_screen_smaller_than_the_design_gets_most_of_its_work_area()
    {
        // A laptop at 125 %: 1536 x 824 to give.
        var work = new Rectangle(0, 0, 1920, 1030);

        (Rectangle bounds, bool maximize) = WindowFit.Centered(work, 1.25, Designed, Smallest);

        Assert.Equal(1650, bounds.Width);
        Assert.Equal((int)Math.Round(824 * 0.94 * 1.25), bounds.Height);
        Assert.True(work.Contains(bounds));
        Assert.False(maximize);
    }

    [Fact]
    public void A_screen_too_small_for_the_smallest_size_asks_for_maximized_and_stays_within_it()
    {
        var work = new Rectangle(-1366, 0, 1366, 728);

        (Rectangle bounds, bool maximize) = WindowFit.Centered(work, 1, Designed, Smallest);

        Assert.True(maximize);
        Assert.True(work.Contains(bounds));
    }
}
