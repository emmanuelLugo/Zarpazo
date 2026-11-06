using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Editor;

/// <summary>Replaces an area with large flat blocks of its own average color, to hide sensitive data.</summary>
internal sealed class PixelateAnnotation(Color color, double thickness, Point start, ScreenSource screen)
    : DragAnnotation(color, thickness, start)
{
    private const int BlockSize = 12;
    private const int BytesPerPixel = 4;

    // Draw runs on every repaint, but the pixels only change while dragging.
    private (Int32Rect Area, BitmapSource Image)? _cache;

    public override void Draw(DrawingContext context)
    {
        var box = new Rect(Start, End);
        Int32Rect area = ToPixels(box);
        if (area.Width < 1 || area.Height < 1)
        {
            return;
        }

        if (_cache is not { } cache || !cache.Area.Equals(area))
        {
            cache = (area, Pixelate(area));
            _cache = cache;
        }
        context.DrawImage(cache.Image, box);
    }

    private Int32Rect ToPixels(Rect box)
    {
        int imageWidth = screen.Image.PixelWidth;
        int imageHeight = screen.Image.PixelHeight;
        int x = Math.Clamp((int)Math.Floor(box.Left * screen.ScaleX), 0, imageWidth - 1);
        int y = Math.Clamp((int)Math.Floor(box.Top * screen.ScaleY), 0, imageHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(box.Right * screen.ScaleX), x, imageWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(box.Bottom * screen.ScaleY), y, imageHeight);
        return new Int32Rect(x, y, right - x, bottom - y);
    }

    private BitmapSource Pixelate(Int32Rect area)
    {
        int stride = area.Width * BytesPerPixel;
        var pixels = new byte[stride * area.Height];
        screen.Image.CopyPixels(area, pixels, stride, 0);

        for (int top = 0; top < area.Height; top += BlockSize)
        {
            for (int left = 0; left < area.Width; left += BlockSize)
            {
                int width = Math.Min(BlockSize, area.Width - left);
                int height = Math.Min(BlockSize, area.Height - top);

                long blue = 0, green = 0, red = 0;
                for (int row = top; row < top + height; row++)
                {
                    for (int column = left; column < left + width; column++)
                    {
                        int i = row * stride + column * BytesPerPixel;
                        blue += pixels[i];
                        green += pixels[i + 1];
                        red += pixels[i + 2];
                    }
                }

                int count = width * height;
                for (int row = top; row < top + height; row++)
                {
                    for (int column = left; column < left + width; column++)
                    {
                        int i = row * stride + column * BytesPerPixel;
                        pixels[i] = (byte)(blue / count);
                        pixels[i + 1] = (byte)(green / count);
                        pixels[i + 2] = (byte)(red / count);
                    }
                }
            }
        }

        BitmapSource image = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        image.Freeze();
        return image;
    }
}
