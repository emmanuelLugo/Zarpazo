using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>A numbered badge for marking steps. Placed with a click, not a drag.</summary>
internal sealed class NumberAnnotation(Color color, double thickness, Point center, int number)
    : Annotation(color, thickness)
{
    private const double LightBackgroundLuminance = 0.6;

    public int Number => number;

    public override bool IsEmpty => false;

    private double Radius => 8 + Stroke.Thickness * 2;

    public override Rect Bounds => new(center.X - Radius, center.Y - Radius, Radius * 2, Radius * 2);

    public override void Offset(Vector delta) => center += delta;

    public override bool HitTest(Point point, double tolerance) => (point - center).Length <= Radius + tolerance;

    public override void Draw(DrawingContext context)
    {
        context.DrawEllipse(Stroke.Brush, null, center, Radius, Radius);

        // Dark digits on light badges (yellow, white) and white digits on dark ones.
        Color fill = ((SolidColorBrush)Stroke.Brush).Color;
        double luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255;
        var digitBrush = luminance > LightBackgroundLuminance ? Brushes.Black : Brushes.White;
        var digits = new FormattedText(
            number.ToString(CultureInfo.CurrentUICulture),
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(TextAnnotation.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Radius * 1.2,
            digitBrush,
            1.0);
        context.DrawText(digits, new Point(center.X - digits.Width / 2, center.Y - digits.Height / 2));
    }
}
