using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Guide;

/// <summary>Joins the pictures into one tall image, one under the other on a light background, for places that take a single image.</summary>
internal static class GuideStack
{
    private const double Gap = 16;
    private const double Padding = 16;
    private const double MaxHeight = 16000; // what the renderer accepts; a longer group is shrunk to fit

    public static BitmapSource Join(IReadOnlyList<BitmapSource> pictures)
    {
        double width = pictures.Max(picture => (double)picture.PixelWidth);
        double height = pictures.Sum(picture => (double)picture.PixelHeight) + Gap * (pictures.Count - 1);
        double scale = Math.Min(1, (MaxHeight - 2 * Padding) / height);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        double y = Padding;
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF4)), null,
                new Rect(0, 0, width * scale + 2 * Padding, height * scale + 2 * Padding));
            foreach (BitmapSource picture in pictures)
            {
                double w = picture.PixelWidth * scale;
                double h = picture.PixelHeight * scale;
                context.DrawImage(picture, new Rect(Padding, y, w, h));
                y += h + Gap * scale;
            }
        }

        var result = new RenderTargetBitmap((int)Math.Ceiling(width * scale + 2 * Padding), (int)Math.Ceiling(height * scale + 2 * Padding), 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
