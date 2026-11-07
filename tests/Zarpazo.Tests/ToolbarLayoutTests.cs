using System.Windows;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class ToolbarLayoutTests
{
    private static readonly Size Screen = new(1366, 768);
    private static readonly Size Tools = new(44, 500);
    private static readonly Size Actions = new(340, 44);

    private static Rect RectOf(Point corner, Size size) => new(corner, size);

    [Fact]
    public void Tools_stand_right_of_the_area_level_with_its_top_and_actions_go_under_it_right_aligned()
    {
        var area = new Rect(200, 100, 400, 300);

        ToolbarPlacement placed = ToolbarLayout.Place(area, Screen, Tools, Actions);

        Assert.True(placed.ToolsOnRight);
        Assert.Equal(new Point(area.Right + ToolbarLayout.Gap, area.Top), placed.Tools);
        Assert.Equal(new Point(area.Right - Actions.Width, area.Bottom + ToolbarLayout.Gap), placed.Actions);
    }

    [Fact]
    public void Tools_move_to_the_left_side_when_the_right_has_no_room()
    {
        var area = new Rect(900, 100, 440, 300); // ends 26 px from the screen edge

        ToolbarPlacement placed = ToolbarLayout.Place(area, Screen, Tools, Actions);

        Assert.False(placed.ToolsOnRight);
        Assert.Equal(area.Left - Tools.Width - ToolbarLayout.Gap, placed.Tools.X);
    }

    [Fact]
    public void Actions_go_above_the_area_when_there_is_no_room_below()
    {
        var area = new Rect(200, 500, 400, 240); // bottom at 740, the row would end at 790

        ToolbarPlacement placed = ToolbarLayout.Place(area, Screen, Tools, Actions);

        Assert.Equal(area.Top - Actions.Height - ToolbarLayout.Gap, placed.Actions.Y);
    }

    [Fact]
    public void Tools_are_pulled_up_so_they_do_not_run_off_the_bottom_of_the_screen()
    {
        var area = new Rect(200, 600, 400, 100);

        ToolbarPlacement placed = ToolbarLayout.Place(area, Screen, Tools, Actions);

        Assert.Equal(Screen.Height - Tools.Height, placed.Tools.Y);
    }

    [Fact]
    public void An_area_that_fills_the_screen_gets_both_bars_inside_it_and_on_screen()
    {
        var area = new Rect(0, 0, Screen.Width, Screen.Height);

        ToolbarPlacement placed = ToolbarLayout.Place(area, Screen, Tools, Actions);

        var screen = new Rect(new Point(0, 0), Screen);
        Assert.True(screen.Contains(RectOf(placed.Tools, Tools)));
        Assert.True(screen.Contains(RectOf(placed.Actions, Actions)));
        Assert.False(RectOf(placed.Tools, Tools).IntersectsWith(RectOf(placed.Actions, Actions)));
    }

    [Theory]
    [InlineData(10, 10, 60, 40)]
    [InlineData(1300, 700, 60, 60)]
    [InlineData(600, 300, 700, 400)]
    [InlineData(0, 300, 1366, 100)]
    [InlineData(500, 0, 300, 768)]
    public void The_bars_stay_on_screen_and_never_cover_each_other(double x, double y, double width, double height)
    {
        var area = new Rect(x, y, width, height);

        ToolbarPlacement placed = ToolbarLayout.Place(area, Screen, Tools, Actions);

        var screen = new Rect(new Point(0, 0), Screen);
        Rect tools = RectOf(placed.Tools, Tools);
        Rect actions = RectOf(placed.Actions, Actions);
        Assert.True(screen.Contains(tools), $"tools {tools}");
        Assert.True(screen.Contains(actions), $"actions {actions}");
        Assert.False(tools.IntersectsWith(actions), $"tools {tools} over actions {actions}");
    }

    [Fact]
    public void The_toolbar_sits_where_the_layout_says_and_every_tool_is_still_reachable()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            toolbar.Place(new Rect(200, 100, 400, 300), Screen);

            double toolsLeft = System.Windows.Controls.Canvas.GetLeft(toolbar.ToolsBar);
            double actionsTop = System.Windows.Controls.Canvas.GetTop(toolbar.ActionsBar);

            Assert.Equal(Screen.Width, toolbar.Width);
            Assert.True(toolsLeft > 600);
            Assert.True(actionsTop > 400);
            Assert.True(toolbar.ToolsBar.DesiredSize.Height > toolbar.ToolsBar.DesiredSize.Width); // a column
            Assert.True(toolbar.ActionsBar.DesiredSize.Width > toolbar.ActionsBar.DesiredSize.Height); // a row
            Assert.True(toolbar.ActionsBar.DesiredSize.Width < 450); // the point of it: no longer a 900 px strip
        });
    }
}
