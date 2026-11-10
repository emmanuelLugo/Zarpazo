using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Zarpazo.Editor;
using Zarpazo.Overlay;

namespace Zarpazo.Tests;

public class AnnotationResizeTests
{
    private static RectangleAnnotation Frame(double x0, double y0, double x1, double y1)
    {
        var rectangle = new RectangleAnnotation(Colors.Red, 4, new Point(x0, y0));
        rectangle.Extend(new Point(x1, y1));
        return rectangle;
    }

    [Fact]
    public void Dragging_the_south_east_grip_moves_only_the_far_corner()
    {
        Rect before = new(100, 100, 200, 100);

        Rect after = SelectionGeometry.Stretch(before, SelectionHandle.SouthEast, new Vector(50, 30), new Rect(0, 0, 1000, 1000));

        Assert.Equal(new Rect(100, 100, 250, 130), after);
    }

    [Fact]
    public void A_grip_cannot_cross_the_opposite_side_or_leave_the_area()
    {
        var area = new Rect(0, 0, 400, 400);
        Rect before = new(100, 100, 200, 100);

        Rect crossed = SelectionGeometry.Stretch(before, SelectionHandle.West, new Vector(500, 0), area);
        Rect outside = SelectionGeometry.Stretch(before, SelectionHandle.East, new Vector(500, 0), area);

        Assert.Equal(300, crossed.Left); // stops flat against the right side
        Assert.Equal(0, crossed.Width);
        Assert.Equal(400, outside.Right);
    }

    [Fact]
    public void An_annotation_that_began_outside_the_area_can_still_be_stretched()
    {
        var area = new Rect(0, 0, 400, 400);
        Rect hanging = new(350, 100, 200, 50); // sticks out past the right edge

        Rect after = SelectionGeometry.Stretch(hanging, SelectionHandle.North, new Vector(0, -20), area);

        Assert.Equal(new Rect(350, 80, 200, 70), after);
    }

    [Fact]
    public void A_rectangle_stretches_to_the_new_box()
    {
        RectangleAnnotation rectangle = Frame(100, 100, 300, 200);

        rectangle.Resize(rectangle.Frame, new Rect(100, 100, 400, 50));

        Assert.Equal(new Rect(100, 100, 400, 50), rectangle.Frame);
    }

    [Fact]
    public void An_arrow_keeps_its_direction_when_stretched()
    {
        var arrow = new ArrowAnnotation(Colors.Red, 4, new Point(300, 200));
        arrow.Extend(new Point(100, 100)); // drawn right to left, bottom to top

        arrow.Resize(arrow.Frame, new Rect(100, 100, 400, 200));

        Assert.Equal(new Point(500, 300), arrow.From);
        Assert.Equal(new Point(100, 100), arrow.To);
    }

    [Fact]
    public void A_flat_line_only_moves_when_its_flat_side_is_stretched()
    {
        var line = new LineAnnotation(Colors.Red, 4, new Point(100, 100));
        line.Extend(new Point(300, 100)); // horizontal: no height to scale

        line.Resize(line.Frame, new Rect(100, 150, 400, 80));

        Assert.Equal(new Point(100, 150), line.From);
        Assert.Equal(new Point(500, 150), line.To);
    }

    [Fact]
    public void A_pen_stroke_scales_every_point()
    {
        var pen = new PenAnnotation(Colors.Red, 4, new Point(0, 0));
        pen.Extend(new Point(50, 100));
        pen.Extend(new Point(100, 0));

        pen.Resize(pen.Frame, new Rect(0, 0, 200, 50));

        Assert.Equal([new Point(0, 0), new Point(100, 50), new Point(200, 0)], pen.Points);
    }

    [Fact]
    public void Text_steps_and_the_ruler_are_moved_but_not_stretched()
    {
        Assert.False(new TextAnnotation(Colors.Red, 4, new Point(0, 0), "hola").CanResize);
        Assert.False(new NumberAnnotation(Colors.Red, 4, new Point(0, 0), 1).CanResize);
        Assert.True(Frame(0, 0, 10, 10).CanResize);
    }

    [Fact]
    public void Undo_and_redo_stretch_the_annotation_back_and_forth()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            RectangleAnnotation rectangle = Frame(100, 100, 300, 200);
            canvas.Commit(rectangle);
            Rect before = rectangle.Frame;

            rectangle.Resize(before, new Rect(100, 100, 400, 300));
            canvas.RecordResize(rectangle, before, rectangle.Frame);
            Assert.Equal(new Rect(100, 100, 400, 300), rectangle.Frame);

            canvas.Undo();
            Assert.Equal(before, rectangle.Frame);

            canvas.Redo();
            Assert.Equal(new Rect(100, 100, 400, 300), rectangle.Frame);
        });
    }

    [Fact]
    public void The_grips_are_found_at_the_corners_and_the_middle_of_each_side_of_the_selected_annotation()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            RectangleAnnotation rectangle = Frame(100, 100, 300, 200);
            canvas.Commit(rectangle);
            Assert.Equal(SelectionHandle.None, canvas.HandleAt(new Point(100, 100), 6)); // nothing picked yet

            canvas.Selected = rectangle;
            Rect outline = rectangle.Bounds;
            outline.Inflate(3, 3);

            Assert.Equal(SelectionHandle.NorthWest, canvas.HandleAt(outline.TopLeft, 6));
            Assert.Equal(SelectionHandle.East, canvas.HandleAt(new Point(outline.Right, 150), 6));
            Assert.Equal(SelectionHandle.None, canvas.HandleAt(new Point(200, 150), 6)); // the middle
        });
    }

    [Fact]
    public void Every_tool_has_its_own_letter()
    {
        var keys = Enum.GetValues<AnnotationTool>().Select(ToolShortcuts.KeyFor).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(AnnotationTool.Rectangle, ToolShortcuts.ForKey(Key.R));
        Assert.Null(ToolShortcuts.ForKey(Key.F)); // F selects the whole monitor
        Assert.Null(ToolShortcuts.ForKey(Key.X));  // X switches the color format of the picker
        Assert.Equal("Flecha (A)", ToolShortcuts.WithKey("Flecha", AnnotationTool.Arrow));
    }

    [Fact]
    public void Retyping_a_text_changes_its_words_and_undo_brings_the_old_ones_back()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            var text = new TextAnnotation(Colors.Red, 4, new Point(10, 10), "hola");
            canvas.Commit(text);

            canvas.EditText(text, "adiós");
            Assert.Equal("adiós", text.Text);

            canvas.Undo();
            Assert.Equal("hola", text.Text);

            canvas.Redo();
            Assert.Equal("adiós", text.Text);
        });
    }

    [Fact]
    public void Retyping_the_same_words_adds_nothing_to_the_history()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            var text = new TextAnnotation(Colors.Red, 4, new Point(10, 10), "hola");
            canvas.Commit(text);

            canvas.EditText(text, "hola");
            canvas.Undo(); // undoes the commit, not an edit that never happened

            Assert.Empty(canvas.Items);
        });
    }
}
