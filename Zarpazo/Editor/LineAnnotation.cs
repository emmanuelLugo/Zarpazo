using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal class LineAnnotation(Color color, double thickness, Point start)
    : DragAnnotation(color, thickness, start)
{
    public override void Draw(DrawingContext context) => context.DrawLine(Stroke, Start, End);
}
