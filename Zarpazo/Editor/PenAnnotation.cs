using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class PenAnnotation : Annotation
{
    private readonly List<Point> _points;

    public PenAnnotation(Color color, double thickness, Point start) : base(color, thickness) =>
        _points = [start];

    public override bool IsEmpty => _points.Count < 2;

    public override Rect Bounds
    {
        get
        {
            var box = new Rect(
                new Point(_points.Min(p => p.X), _points.Min(p => p.Y)),
                new Point(_points.Max(p => p.X), _points.Max(p => p.Y)));
            box.Inflate(Stroke.Thickness / 2, Stroke.Thickness / 2);
            return box;
        }
    }

    public override void Extend(Point point) => _points.Add(point);

    public override void Offset(Vector delta)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] += delta;
        }
    }

    // Close to any piece of the stroke, not just anywhere inside its bounding box.
    public override bool HitTest(Point point, double tolerance)
    {
        double reach = tolerance + Stroke.Thickness / 2;
        for (int i = 1; i < _points.Count; i++)
        {
            if (DistanceToSegment(point, _points[i - 1], _points[i]) <= reach)
            {
                return true;
            }
        }
        return false;
    }

    public override void Draw(DrawingContext context)
    {
        if (IsEmpty)
        {
            return;
        }

        var path = new StreamGeometry();
        using (StreamGeometryContext figure = path.Open())
        {
            figure.BeginFigure(_points[0], false, false);
            figure.PolyLineTo(_points.Skip(1).ToList(), true, true);
        }
        path.Freeze();
        context.DrawGeometry(null, Stroke, path);
    }
}
