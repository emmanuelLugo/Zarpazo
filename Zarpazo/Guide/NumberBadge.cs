using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Guide;

/// <summary>Puts the capture's number in its top-left corner, so whoever receives several pictures knows the order.</summary>
internal static class NumberBadge
{
    private const double Radius = 15;
    private const double Margin = 10;

    public static BitmapSource Apply(BitmapSource picture, int number)
    {
        double radius = Math.Min(Radius, Math.Min(picture.PixelWidth, picture.PixelHeight) / 3.0); // a tiny picture gets a tiny badge
        var center = new Point(Margin / 2 + radius, Margin / 2 + radius);

        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(picture, new Rect(0, 0, picture.PixelWidth, picture.PixelHeight));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), new Pen(Brushes.White, 2), center, radius, radius);
            var digits = new FormattedText(
                number.ToString(CultureInfo.CurrentUICulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                radius * (number > 9 ? 1.0 : 1.2),
                Brushes.White,
                1.0);
            context.DrawText(digits, new Point(center.X - digits.Width / 2, center.Y - digits.Height / 2));
        }

        var result = new RenderTargetBitmap(picture.PixelWidth, picture.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
