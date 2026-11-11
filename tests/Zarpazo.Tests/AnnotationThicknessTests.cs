using System.Windows;
using System.Windows.Media;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class AnnotationThicknessTests
{
    private static LineAnnotation Line(double thickness = 4)
    {
        var line = new LineAnnotation(Colors.Red, thickness, new Point(0, 0));
        line.Extend(new Point(100, 0));
        return line;
    }

    [Fact]
    public void Changing_the_thickness_keeps_the_color()
    {
        LineAnnotation line = Line();

        line.SetThickness(9);

        Assert.Equal(9, line.Thickness);
        Assert.Equal(Colors.Red, line.Color);
    }

    [Fact]
    public void Text_and_step_badges_grow_with_the_thickness()
    {
        var text = new TextAnnotation(Colors.Red, 4, new Point(0, 0), "hola");
        var step = new NumberAnnotation(Colors.Red, 4, new Point(50, 50), 1);
        double textHeight = text.Bounds.Height;
        double stepWidth = step.Bounds.Width;

        text.SetThickness(10);
        step.SetThickness(10);

        Assert.True(text.Bounds.Height > textHeight);
        Assert.True(step.Bounds.Width > stepWidth);
    }

    [Fact]
    public void Only_the_marks_that_have_a_stroke_or_a_size_use_the_wheel()
    {
        Assert.True(Line().UsesThickness);
        Assert.True(StrokeSize.UsesWheel(AnnotationTool.Pen));
        Assert.True(StrokeSize.UsesWheel(AnnotationTool.Text));
        Assert.False(StrokeSize.UsesWheel(AnnotationTool.Highlighter));
        Assert.False(StrokeSize.UsesWheel(AnnotationTool.Pixelate));
        Assert.False(StrokeSize.UsesWheel(AnnotationTool.Move));
        Assert.False(new HighlighterAnnotation(Colors.Yellow, 4, new Point(0, 0)).UsesThickness);
    }

    [Fact]
    public void The_size_is_kept_inside_its_limits()
    {
        Assert.Equal(StrokeSize.Min, StrokeSize.Clamp(-5));
        Assert.Equal(StrokeSize.Max, StrokeSize.Clamp(1000));
        Assert.Equal(StrokeSize.Default, StrokeSize.Clamp(double.NaN));
        Assert.Equal(7, StrokeSize.Clamp(7));
    }

    [Fact]
    public void A_burst_of_wheel_notches_on_a_placed_annotation_is_undone_in_one_step()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            LineAnnotation line = Line();
            canvas.Commit(line);

            canvas.ChangeThickness(line, 5);
            canvas.ChangeThickness(line, 6);
            canvas.ChangeThickness(line, 7);
            Assert.Equal(7, line.Thickness);

            canvas.Undo();
            Assert.Equal(4, line.Thickness);

            canvas.Redo();
            Assert.Equal(7, line.Thickness);
        });
    }

    [Fact]
    public void The_thickness_of_a_drag_in_progress_changes_without_touching_the_history()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            LineAnnotation placed = Line();
            canvas.Commit(placed);
            LineAnnotation dragging = Line();
            canvas.Begin(dragging);

            canvas.ChangeThickness(dragging, 12);
            canvas.Commit(dragging);
            canvas.Undo(); // removes the line, it does not just shrink it back

            Assert.Equal(12, dragging.Thickness);
            Assert.Single(canvas.Items);
        });
    }

    [Fact]
    public void A_change_beyond_the_limits_stops_at_the_limit()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            LineAnnotation line = Line();
            canvas.Commit(line);

            canvas.ChangeThickness(line, 999);

            Assert.Equal(StrokeSize.Max, line.Thickness);
        });
    }
}
