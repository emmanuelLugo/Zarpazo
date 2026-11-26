using System.Windows;
using System.Windows.Media;
using Xunit;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class CaptureEditingTests
{
    [Fact]
    public void Arrow_key_steps_in_a_row_move_an_annotation_and_are_undone_as_one()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            var box = new RectangleAnnotation(Colors.Red, 3, new Point(10, 10));
            box.Extend(new Point(50, 40));
            canvas.Commit(box);

            canvas.Nudge(box, new Vector(1, 0));
            canvas.Nudge(box, new Vector(0, 10));
            Assert.Equal(new Point(11, 20), box.Frame.TopLeft);

            canvas.Undo();
            Assert.Equal(new Point(10, 10), box.Frame.TopLeft);
            Assert.Single(canvas.Items); // only the steps were undone, not the drawing
        });
    }

    [Fact]
    public void A_placed_annotation_changes_color_and_undo_brings_the_old_one_back()
    {
        Sta.Run(() =>
        {
            var canvas = new AnnotationCanvas();
            var box = new RectangleAnnotation(Colors.Red, 3, new Point(10, 10));
            box.Extend(new Point(50, 40));
            canvas.Commit(box);

            canvas.ChangeColor(box, Colors.Blue);
            Assert.Equal(Colors.Blue, box.Color);
            Assert.Equal(3, box.Thickness);

            canvas.Undo();
            Assert.Equal(Colors.Red, box.Color);
            canvas.Redo();
            Assert.Equal(Colors.Blue, box.Color);
        });
    }
}
