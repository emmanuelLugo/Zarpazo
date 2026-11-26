using System.Windows;
using System.Windows.Media;
using Xunit;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class CaptureEditingTests
{
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
