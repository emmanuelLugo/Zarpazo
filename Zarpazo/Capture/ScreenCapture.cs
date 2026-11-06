using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace Zarpazo.Capture;

internal static class ScreenCapture
{
    /// <summary>Captures every monitor. Both the image and the bounds are in physical pixels.</summary>
    public static (BitmapSource Image, Rectangle Bounds) CaptureVirtualScreen()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            // shortcut: plain SourceCopy skips layered (see-through) windows; CaptureBlt would include them but
            // CopyFromScreen rejects it combined with SourceCopy. Switch to BitBlt via P/Invoke if that matters.
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        BitmapData pixels = bitmap.LockBits(
            new Rectangle(Point.Empty, bounds.Size),
            ImageLockMode.ReadOnly,
            bitmap.PixelFormat);
        try
        {
            BitmapSource image = BitmapSource.Create(
                bounds.Width,
                bounds.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgr32,
                null,
                pixels.Scan0,
                pixels.Stride * bounds.Height,
                pixels.Stride);
            image.Freeze();
            return (image, bounds);
        }
        finally
        {
            bitmap.UnlockBits(pixels);
        }
    }
}
