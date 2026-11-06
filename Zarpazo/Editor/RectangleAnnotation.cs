using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class RectangleAnnotation(Color color, double thickness, Point start)
    : DragAnnotation(color, thickness, start)
{
    public override void Draw(DrawingContext context) => context.DrawRectangle(null, Stroke, new Rect(Start, End));
}
