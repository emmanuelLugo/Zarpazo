using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Drawing.Point;

namespace Zarpazo.Capture;

internal static class EditBackdrop
{
    /// <summary>
    /// The screen as it is now with an earlier capture drawn back where it was taken, so the overlay can edit it in place.
    /// If a monitor is gone and the old position is off the screen, the capture is moved to the nearest place that fits.
    /// </summary>
    /// <param name="screen">The whole virtual screen, one image pixel per physical pixel.</param>
    /// <param name="bounds">The virtual screen in physical pixels.</param>
    /// <returns>The new image, in the same pixel format as <paramref name="screen"/>, and where the capture ended up.</returns>
    public static (BitmapSource Image, Point Origin) Place(BitmapSource screen, Rectangle bounds, BitmapSource capture, int left, int top)
    {
        left = Math.Max(bounds.Left, Math.Min(left, bounds.Right - capture.PixelWidth));
        top = Math.Max(bounds.Top, Math.Min(top, bounds.Bottom - capture.PixelHeight));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor); // copy pixels, never smooth them
        using (DrawingContext context = visual.RenderOpen())
        {
            // Rectangles in pixels: the images' own DPI must not change their size.
            context.DrawImage(screen, new Rect(0, 0, screen.PixelWidth, screen.PixelHeight));
            context.DrawImage(capture, new Rect(left - bounds.X, top - bounds.Y, capture.PixelWidth, capture.PixelHeight));
        }

        var rendered = new RenderTargetBitmap(screen.PixelWidth, screen.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);

        // The magnifier and the pixelate tool read the screenshot as Bgr32.
        var image = new FormatConvertedBitmap(rendered, PixelFormats.Bgr32, null, 0);
        image.Freeze();
        return (image, new Point(left, top));
    }
}
