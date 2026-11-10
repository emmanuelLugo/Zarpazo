using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Guide;

/// <summary>Draws where the click was on a step's picture: a ring around the point and a small numbered badge beside it.</summary>
internal static class ClickMarker
{
    private const double Radius = 22;
    private const double BadgeRadius = 11;

    private static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);

    /// <param name="at">The click, in pixels of <paramref name="picture"/>.</param>
    public static BitmapSource Apply(BitmapSource picture, Point at, int number)
    {
        var ring = new Pen(new SolidColorBrush(Red), 4);
        var tint = new SolidColorBrush(Color.FromArgb(0x33, Red.R, Red.G, Red.B));
        // Keep the badge inside the picture: up and left of the ring, unless that would leave it.
        var badge = new Point(Math.Max(BadgeRadius, at.X - Radius), Math.Max(BadgeRadius, at.Y - Radius));

        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(picture, new Rect(0, 0, picture.PixelWidth, picture.PixelHeight));
            context.DrawEllipse(tint, ring, at, Radius, Radius);
            context.DrawEllipse(new SolidColorBrush(Red), new Pen(Brushes.White, 2), badge, BadgeRadius, BadgeRadius);
            var digits = new FormattedText(
                number.ToString(CultureInfo.CurrentUICulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                BadgeRadius * (number > 9 ? 1.0 : 1.25),
                Brushes.White,
                1.0);
            context.DrawText(digits, new Point(badge.X - digits.Width / 2, badge.Y - digits.Height / 2));
        }

        var result = new RenderTargetBitmap(picture.PixelWidth, picture.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
