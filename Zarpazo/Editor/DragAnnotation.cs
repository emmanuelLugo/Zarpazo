using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>An annotation defined by the point where the drag started and the one where it is now.</summary>
internal abstract class DragAnnotation : Annotation
{
    protected Point Start { get; private set; }
    protected Point End { get; private set; }

    protected DragAnnotation(Color color, double thickness, Point start) : base(color, thickness)
    {
        Start = start;
        End = start;
    }

    public override bool IsEmpty => Start == End;

    public override Rect Bounds
    {
        get
        {
            var box = new Rect(Start, End);
            box.Inflate(Stroke.Thickness / 2, Stroke.Thickness / 2);
            return box;
        }
    }

    public override void Extend(Point point) => End = point;

    public override void Offset(Vector delta)
    {
        Start += delta;
        End += delta;
    }

    /// <summary>How far from the shape's lines a click still counts as touching it.</summary>
    protected double Reach(double tolerance) => tolerance + Stroke.Thickness / 2;
}
