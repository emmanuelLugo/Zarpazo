using System.Windows;
using System.Windows.Media;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class AnnotationEditingTests
{
    private const double Tolerance = 6;
    private static readonly Color Red = Colors.Red;

    private static void OnSta(Action test) => Sta.Run(test);

    private static Annotation Drag(Annotation annotation, double x, double y)
    {
        annotation.Extend(new Point(x, y));
        return annotation;
    }

    [Fact]
    public void A_rectangle_is_hit_on_its_outline_but_not_inside()
    {
        Annotation frame = Drag(new RectangleAnnotation(Red, 4, new Point(100, 100)), 300, 300);

        Assert.True(frame.HitTest(new Point(100, 200), Tolerance)); // left edge
        Assert.True(frame.HitTest(new Point(200, 304), Tolerance)); // just outside the bottom edge
        Assert.False(frame.HitTest(new Point(200, 200), Tolerance)); // the middle, so things inside stay pickable
        Assert.False(frame.HitTest(new Point(400, 200), Tolerance));
    }

    [Fact]
    public void A_very_thin_rectangle_is_still_hit_instead_of_throwing()
    {
        Annotation sliver = Drag(new RectangleAnnotation(Red, 2, new Point(100, 100)), 300, 102);
        Assert.True(sliver.HitTest(new Point(200, 101), Tolerance));
    }

    [Fact]
    public void A_line_is_hit_near_the_segment_and_not_beyond_its_ends()
    {
        Annotation line = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);

        Assert.True(line.HitTest(new Point(50, 5), Tolerance));
        Assert.True(line.HitTest(new Point(105, 0), Tolerance));
        Assert.False(line.HitTest(new Point(50, 20), Tolerance));
        Assert.False(line.HitTest(new Point(130, 0), Tolerance));
    }

    [Fact]
    public void An_ellipse_is_hit_on_its_outline_but_not_at_the_center()
    {
        Annotation ellipse = Drag(new EllipseAnnotation(Red, 4, new Point(0, 0)), 200, 100);

        Assert.True(ellipse.HitTest(new Point(100, 0), Tolerance)); // top
        Assert.True(ellipse.HitTest(new Point(200, 50), Tolerance)); // right
        Assert.False(ellipse.HitTest(new Point(100, 50), Tolerance)); // center
        Assert.False(ellipse.HitTest(new Point(5, 5), Tolerance)); // the bounding-box corner is outside the ellipse
    }

    [Fact]
    public void A_pen_stroke_is_hit_along_its_path_and_not_across_its_bounding_box()
    {
        var pen = new PenAnnotation(Red, 4, new Point(0, 0));
        pen.Extend(new Point(100, 0));
        pen.Extend(new Point(100, 100));

        Assert.True(pen.HitTest(new Point(50, 0), Tolerance));
        Assert.True(pen.HitTest(new Point(100, 60), Tolerance));
        Assert.False(pen.HitTest(new Point(20, 80), Tolerance)); // inside the box, far from the stroke
    }

    [Fact]
    public void A_step_badge_is_hit_inside_its_circle()
    {
        var badge = new NumberAnnotation(Red, 4, new Point(50, 50), 1); // radius 16
        Assert.True(badge.HitTest(new Point(60, 50), Tolerance));
        Assert.False(badge.HitTest(new Point(90, 50), Tolerance));
    }

    [Fact]
    public void A_ruler_is_hit_on_its_length_label_as_well_as_on_the_line()
    {
        OnSta(() =>
        {
            var ruler = new RulerAnnotation(Red, 4, new Point(0, 0), new ScreenSource(null!, 2, 2));
            ruler.Extend(new Point(100, 0));

            Assert.True(ruler.HitTest(new Point(20, 0), 0)); // on the line
            Assert.True(ruler.HitTest(new Point(50, 7), 0)); // on the label, off the line
            Assert.False(ruler.HitTest(new Point(20, 7), 0));
            Assert.True(ruler.Bounds.Height > 10); // the outline wraps the label too
        });
    }

    [Fact]
    public void A_ruler_without_dragging_is_empty()
    {
        OnSta(() => Assert.True(new RulerAnnotation(Red, 4, new Point(5, 5), new ScreenSource(null!, 1, 1)).IsEmpty));
    }

    [Fact]
    public void Offset_moves_the_annotation_and_its_hit_area()
    {
        Annotation line = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
        line.Offset(new Vector(0, 50));

        Assert.True(line.HitTest(new Point(50, 50), Tolerance));
        Assert.False(line.HitTest(new Point(50, 0), Tolerance));
    }

    [Fact]
    public void The_topmost_annotation_wins_the_hit()
    {
        OnSta(() =>
        {
            var canvas = new AnnotationCanvas();
            Annotation below = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
            Annotation above = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
            canvas.Commit(below);
            canvas.Commit(above);

            Assert.Same(above, canvas.HitTest(new Point(50, 0), Tolerance));
        });
    }

    [Fact]
    public void Undo_and_redo_restore_a_deleted_annotation_at_its_old_position()
    {
        OnSta(() =>
        {
            var canvas = new AnnotationCanvas();
            Annotation first = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
            Annotation second = Drag(new LineAnnotation(Red, 4, new Point(0, 50)), 100, 50);
            Annotation third = Drag(new LineAnnotation(Red, 4, new Point(0, 100)), 100, 100);
            canvas.Commit(first);
            canvas.Commit(second);
            canvas.Commit(third);

            canvas.Selected = second;
            canvas.DeleteSelected();
            Assert.Null(canvas.HitTest(new Point(50, 50), Tolerance));
            Assert.Null(canvas.Selected);

            canvas.Undo();
            Assert.Same(second, canvas.HitTest(new Point(50, 50), Tolerance));

            canvas.Redo();
            Assert.Null(canvas.HitTest(new Point(50, 50), Tolerance));
        });
    }

    [Fact]
    public void Undo_and_redo_move_an_annotation_back_and_forth()
    {
        OnSta(() =>
        {
            var canvas = new AnnotationCanvas();
            Annotation line = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
            canvas.Commit(line);

            line.Offset(new Vector(0, 40)); // the window moves it live while dragging...
            canvas.RecordMove(line, new Vector(0, 40)); // ...and records it once on release
            Assert.NotNull(canvas.HitTest(new Point(50, 40), Tolerance));

            canvas.Undo();
            Assert.NotNull(canvas.HitTest(new Point(50, 0), Tolerance));
            Assert.Null(canvas.HitTest(new Point(50, 40), Tolerance));

            canvas.Redo();
            Assert.NotNull(canvas.HitTest(new Point(50, 40), Tolerance));
        });
    }

    [Fact]
    public void Undoing_the_creation_of_the_selected_annotation_clears_the_selection()
    {
        OnSta(() =>
        {
            var canvas = new AnnotationCanvas();
            Annotation line = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
            canvas.Commit(line);
            canvas.Selected = line;

            canvas.Undo();

            Assert.Null(canvas.Selected);
        });
    }

    [Fact]
    public void A_new_edit_discards_what_could_have_been_redone()
    {
        OnSta(() =>
        {
            var canvas = new AnnotationCanvas();
            Annotation first = Drag(new LineAnnotation(Red, 4, new Point(0, 0)), 100, 0);
            canvas.Commit(first);
            canvas.Undo();
            canvas.Commit(Drag(new LineAnnotation(Red, 4, new Point(0, 50)), 100, 50));

            canvas.Redo(); // nothing to redo any more

            Assert.Null(canvas.HitTest(new Point(50, 0), Tolerance));
            Assert.NotNull(canvas.HitTest(new Point(50, 50), Tolerance));
        });
    }

    [Fact]
    public void Step_numbers_never_repeat_after_a_badge_is_deleted()
    {
        OnSta(() =>
        {
            var canvas = new AnnotationCanvas();
            Assert.Equal(1, canvas.NextStepNumber);

            var one = new NumberAnnotation(Red, 4, new Point(10, 10), canvas.NextStepNumber);
            canvas.Commit(one);
            var two = new NumberAnnotation(Red, 4, new Point(50, 10), canvas.NextStepNumber);
            canvas.Commit(two);
            Assert.Equal(3, canvas.NextStepNumber);

            canvas.Selected = one;
            canvas.DeleteSelected();
            Assert.Equal(3, canvas.NextStepNumber); // not 2, which would collide with the badge still on screen
        });
    }
}
