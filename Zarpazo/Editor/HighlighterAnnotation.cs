using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>A see-through filled box, like a marker over text.</summary>
internal sealed class HighlighterAnnotation : DragAnnotation
{
    private const byte Alpha = 0x66;

    public HighlighterAnnotation(Color color, double thickness, Point start) : base(color, thickness, start)
    {
    }

    public override bool UsesThickness => false;

    public override void Draw(DrawingContext context) =>
        context.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(Alpha, Color.R, Color.G, Color.B)), null, new Rect(Start, End));
}
