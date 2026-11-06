using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class RectangleAnnotation(Color color, double thickness, Point start)
    : DragAnnotation(color, thickness, start)
{
    public override void Draw(DrawingContext context) => context.DrawRectangle(null, Stroke, new Rect(Start, End));

    // Only the outline counts: a frame drawn around other things must not hide them from the mouse.
    public override bool HitTest(Point point, double tolerance)
    {
        double reach = Reach(tolerance);
        var outer = new Rect(Start, End);
        var inner = outer;
        outer.Inflate(reach, reach);
        inner.Inflate(-reach, -reach); // becomes empty when the rectangle is thinner than the reach
        return outer.Contains(point) && !inner.Contains(point);
    }
}
