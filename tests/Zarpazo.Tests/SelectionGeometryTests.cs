using System.Windows;
using Zarpazo.Overlay;

namespace Zarpazo.Tests;

public class SelectionGeometryTests
{
    private static readonly Rect Area = new(100, 100, 200, 100); // right = 300, bottom = 200
    private static readonly Rect Screen = new(0, 0, 1000, 800);

    [Theory]
    [InlineData(100, 100, SelectionHandle.NorthWest)]
    [InlineData(300, 100, SelectionHandle.NorthEast)]
    [InlineData(100, 200, SelectionHandle.SouthWest)]
    [InlineData(300, 200, SelectionHandle.SouthEast)]
    [InlineData(200, 100, SelectionHandle.North)]
    [InlineData(200, 200, SelectionHandle.South)]
    [InlineData(100, 150, SelectionHandle.West)]
    [InlineData(300, 150, SelectionHandle.East)]
    [InlineData(200, 150, SelectionHandle.Inside)]
    [InlineData(97, 103, SelectionHandle.NorthWest)] // just outside the corner still grabs it
    [InlineData(50, 150, SelectionHandle.None)]
    [InlineData(200, 250, SelectionHandle.None)]
    public void HitTest_finds_the_part_under_the_mouse(double x, double y, SelectionHandle expected) =>
        Assert.Equal(expected, SelectionGeometry.HitTest(Area, new Point(x, y), 6));

    [Fact]
    public void Moving_shifts_the_area_without_resizing_it()
    {
        Rect moved = SelectionGeometry.Apply(Area, SelectionHandle.Inside, new Vector(30, -20), Screen);
        Assert.Equal(new Rect(130, 80, 200, 100), moved);
    }

    [Fact]
    public void Moving_stops_at_the_screen_edges()
    {
        Rect left = SelectionGeometry.Apply(Area, SelectionHandle.Inside, new Vector(-500, 0), Screen);
        Rect bottomRight = SelectionGeometry.Apply(Area, SelectionHandle.Inside, new Vector(5000, 5000), Screen);
        Assert.Equal(0, left.X);
        Assert.Equal(200, left.Width);
        Assert.Equal(1000, bottomRight.Right);
        Assert.Equal(800, bottomRight.Bottom);
    }

    [Fact]
    public void A_corner_moves_only_its_two_edges()
    {
        Rect resized = SelectionGeometry.Apply(Area, SelectionHandle.SouthEast, new Vector(50, 25), Screen);
        Assert.Equal(new Rect(100, 100, 250, 125), resized);
    }

    [Fact]
    public void An_edge_moves_only_that_edge()
    {
        Rect resized = SelectionGeometry.Apply(Area, SelectionHandle.West, new Vector(-40, 999), Screen);
        Assert.Equal(new Rect(60, 100, 240, 100), resized);
    }

    [Fact]
    public void Dragging_past_the_opposite_edge_stops_at_the_minimum_size()
    {
        Rect resized = SelectionGeometry.Apply(Area, SelectionHandle.East, new Vector(-5000, 0), Screen);
        Assert.Equal(SelectionGeometry.MinSize, resized.Width);
        Assert.Equal(100, resized.Left);
    }

    [Fact]
    public void Resizing_stops_at_the_screen_edges()
    {
        Rect resized = SelectionGeometry.Apply(Area, SelectionHandle.NorthWest, new Vector(-5000, -5000), Screen);
        Assert.Equal(0, resized.Left);
        Assert.Equal(0, resized.Top);
        Assert.Equal(300, resized.Right);
        Assert.Equal(200, resized.Bottom);
    }

    [Fact]
    public void A_maximized_window_counts_as_filling_the_monitor_but_a_normal_one_does_not()
    {
        var monitors = new[] { new System.Drawing.Rectangle(0, 0, 1920, 1080), new System.Drawing.Rectangle(1920, 0, 1280, 1024) };

        Assert.True(SelectionGeometry.FillsAMonitor(new System.Drawing.Rectangle(0, 0, 1920, 1040), monitors));   // maximized, taskbar below
        Assert.True(SelectionGeometry.FillsAMonitor(new System.Drawing.Rectangle(1920, 0, 1280, 1024), monitors)); // full screen on the second one
        Assert.False(SelectionGeometry.FillsAMonitor(new System.Drawing.Rectangle(200, 100, 900, 600), monitors));
        Assert.False(SelectionGeometry.FillsAMonitor(new System.Drawing.Rectangle(0, 0, 960, 1040), monitors));    // half a monitor
    }
}
