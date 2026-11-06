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

    public static Annotation Create(AnnotationTool tool, Color color, double thickness, Point start) => tool switch
    {
        AnnotationTool.Rectangle => new RectangleAnnotation(color, thickness, start),
        AnnotationTool.Line => new LineAnnotation(color, thickness, start),
        AnnotationTool.Arrow => new ArrowAnnotation(color, thickness, start),
        AnnotationTool.Pen => new PenAnnotation(color, thickness, start),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "This tool does not draw."),
    };
}
