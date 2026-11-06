using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class EllipseAnnotation(Color color, double thickness, Point start)
    : DragAnnotation(color, thickness, start)
{
    public override void Draw(DrawingContext context)
    {
        var box = new Rect(Start, End);
        context.DrawEllipse(null, Stroke, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, box.Height / 2);
    }
}
