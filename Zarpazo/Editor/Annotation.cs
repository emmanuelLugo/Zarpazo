using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>A drawn object. Coordinates are in the overlay's layout units; the canvas rescales them on export.</summary>
internal abstract class Annotation
{
    protected Pen Stroke { get; }

    protected Annotation(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Stroke = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        Stroke.Freeze();
    }

    /// <summary>True while nothing visible was drawn yet, such as a click without dragging.</summary>
    public abstract bool IsEmpty { get; }

    /// <summary>Follows the mouse while dragging. Annotations that are not drawn by dragging ignore it.</summary>
    public virtual void Extend(Point point)
    {
    }

    public abstract void Draw(DrawingContext context);

    /// <summary>The box around the drawn annotation, used for the selection outline.</summary>
    public abstract Rect Bounds { get; }

    /// <summary>Moves the whole annotation.</summary>
    public abstract void Offset(Vector delta);

    /// <summary>Whether the point is on the annotation. The tolerance makes thin lines easy to pick.</summary>
    public virtual bool HitTest(Point point, double tolerance)
    {
        Rect box = Bounds;
        box.Inflate(tolerance, tolerance);
        return box.Contains(point);
    }

    protected static double DistanceToSegment(Point point, Point from, Point to)
    {
        Vector segment = to - from;
        double lengthSquared = segment.LengthSquared;
        double along = lengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(point - from, segment) / lengthSquared, 0, 1);
        return (point - (from + segment * along)).Length;
    }

    /// <summary>Starts the annotation for tools that are drawn by dragging.</summary>
    public static Annotation Create(AnnotationTool tool, Color color, double thickness, Point start, ScreenSource screen) => tool switch
    {
        AnnotationTool.Rectangle => new RectangleAnnotation(color, thickness, start),
        AnnotationTool.Ellipse => new EllipseAnnotation(color, thickness, start),
        AnnotationTool.Line => new LineAnnotation(color, thickness, start),
        AnnotationTool.Arrow => new ArrowAnnotation(color, thickness, start),
        AnnotationTool.Pen => new PenAnnotation(color, thickness, start),
        AnnotationTool.Highlighter => new HighlighterAnnotation(color, thickness, start),
        AnnotationTool.Pixelate => new PixelateAnnotation(color, thickness, start, screen),
        AnnotationTool.Ruler => new RulerAnnotation(color, thickness, start, screen),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "This tool does not draw."),
    };
}
