using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class PenAnnotation : Annotation
{
    private readonly List<Point> _points;

    public PenAnnotation(Color color, double thickness, Point start) : base(color, thickness) =>
        _points = [start];

    public override bool IsEmpty => _points.Count < 2;

    public override void Extend(Point point) => _points.Add(point);

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
