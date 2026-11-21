using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Output;

internal static class ImageResizer
{
    /// <summary>The scales the editor offers; 1 leaves the capture as it is.</summary>
    public static readonly double[] Choices = [1, 0.75, 0.5, 0.25];

    /// <summary>The picture at <paramref name="factor"/> of its size, smoothed. A factor of 1 returns the same picture untouched.</summary>
    public static BitmapSource Scale(BitmapSource image, double factor)
    {
        if (factor >= 1)
        {
            return image;
        }

        int width = Math.Max(1, (int)Math.Round(image.PixelWidth * factor));
        int height = Math.Max(1, (int)Math.Round(image.PixelHeight * factor));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, width, height)); // pixels, so the image's own DPI can't change the size
        }

        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
