using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>An annotation defined by the point where the drag started and the one where it is now.</summary>
internal abstract class DragAnnotation : Annotation
{
    protected Point Start { get; }
    protected Point End { get; private set; }

    protected DragAnnotation(Color color, double thickness, Point start) : base(color, thickness)
    {
        Start = start;
        End = start;
    }

    public override bool IsEmpty => Start == End;

    public override void Extend(Point point) => End = point;
}
