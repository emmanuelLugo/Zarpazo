using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class TextAnnotation(Color color, double thickness, Point origin, string text)
    : Annotation(color, thickness)
{
    // Shared with the text box used to type it, so what you see while typing is what gets drawn.
    public static readonly FontFamily Font = new("Segoe UI");
    public static readonly FontWeight Weight = FontWeights.SemiBold;

    /// <summary>The thickness buttons double as text sizes: thin, medium and thick give small, medium and large text.</summary>
    public static double SizeFor(double thickness) => 10 + thickness * 3;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(text);

    public override void Draw(DrawingContext context)
    {
        // pixelsPerDip is fixed at 1: the same text is drawn on screen and, scaled, into the saved image.
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(Font, FontStyles.Normal, Weight, FontStretches.Normal),
            SizeFor(Stroke.Thickness),
            Stroke.Brush,
            1.0);
        context.DrawText(formatted, origin);
    }
}
