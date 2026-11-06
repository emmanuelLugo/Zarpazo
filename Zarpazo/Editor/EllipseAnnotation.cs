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

    // Only the outline counts, like the rectangle. A point on the ellipse sits at distance 1 from the center once
    // each axis is scaled to a unit circle; that distance, in pixels, is how far the point is from the line.
    public override bool HitTest(Point point, double tolerance)
    {
        var box = new Rect(Start, End);
        double radiusX = Math.Max(box.Width / 2, 1);
        double radiusY = Math.Max(box.Height / 2, 1);
        double dx = (point.X - (box.X + box.Width / 2)) / radiusX;
        double dy = (point.Y - (box.Y + box.Height / 2)) / radiusY;
        return Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 1) * Math.Min(radiusX, radiusY) <= Reach(tolerance);
    }
}
