using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>A line with end caps that says how long it is in image pixels.</summary>
internal sealed class RulerAnnotation(Color color, double thickness, Point start, ScreenSource screen)
    : LineAnnotation(color, thickness, start)
{
    private const double CapHalfLength = 6;
    private const double LabelPaddingX = 6;
    private const double LabelPaddingY = 2;

    private static readonly Brush LabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0)));

    public override void Draw(DrawingContext context)
    {
        base.Draw(context);

        Vector direction = End - Start;
        if (direction.Length > 0)
        {
            direction.Normalize();
            var cap = new Vector(-direction.Y, direction.X) * CapHalfLength;
            context.DrawLine(Stroke, Start - cap, Start + cap);
            context.DrawLine(Stroke, End - cap, End + cap);
        }

        (FormattedText text, Rect box) = Label();
        context.DrawRoundedRectangle(LabelBackground, null, box, 3, 3);
        context.DrawText(text, new Point(box.X + LabelPaddingX, box.Y + LabelPaddingY));
    }

    public override Rect Bounds => Rect.Union(base.Bounds, Label().Box);

    // Its length is the measurement, so it is moved but not stretched.
    public override bool CanResize => false;

    public override bool HitTest(Point point, double tolerance) =>
        base.HitTest(point, tolerance) || Label().Box.Contains(point);

    // Centered on the line, like a dimension mark. Measured in image pixels, which is what ends up in the saved file.
    private (FormattedText Text, Rect Box) Label()
    {
        double pixels = new Vector((End.X - Start.X) * screen.ScaleX, (End.Y - Start.Y) * screen.ScaleY).Length;
        var text = new FormattedText(
            $"{Math.Round(pixels)} px",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(TextAnnotation.Font, FontStyles.Normal, TextAnnotation.Weight, FontStretches.Normal),
            12,
            Brushes.White,
            1.0);

        var size = new Size(text.Width + 2 * LabelPaddingX, text.Height + 2 * LabelPaddingY);
        var middle = new Point((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);
        return (text, new Rect(middle.X - size.Width / 2, middle.Y - size.Height / 2, size.Width, size.Height));
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
