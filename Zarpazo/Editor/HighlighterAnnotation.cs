using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>A see-through filled box, like a marker over text.</summary>
internal sealed class HighlighterAnnotation : DragAnnotation
{
    private const byte Alpha = 0x66;
    private readonly Brush _fill;

    public HighlighterAnnotation(Color color, double thickness, Point start) : base(color, thickness, start)
    {
        _fill = new SolidColorBrush(Color.FromArgb(Alpha, color.R, color.G, color.B));
        _fill.Freeze();
    }

    public override void Draw(DrawingContext context) => context.DrawRectangle(_fill, null, new Rect(Start, End));
}
