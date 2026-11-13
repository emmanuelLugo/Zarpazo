using System.Windows;

namespace Zarpazo.Recording;

/// <summary>
/// Makes one picture of the zoomed video: the part of the recorded picture the camera shows, stretched smoothly (bilinear)
/// to the full size. With a crop, the camera moves over the rectangle kept, and nothing outside it is ever sampled.
/// </summary>
internal static class ZoomResampler
{
    /// <param name="source">BGRA, width x height, top row first.</param>
    /// <param name="target">The same size as the source; it is filled whole.</param>
    public static void Render(byte[] source, byte[] target, int width, int height, ZoomView view) =>
        Render(source, width, height, new Int32Rect(0, 0, width, height), target, width, height, view);

    /// <param name="source">BGRA, <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/>, top row first.</param>
    /// <param name="area">The rectangle of the source that is kept (the crop); the view is in shares of it.</param>
    /// <param name="target">BGRA, <paramref name="width"/> x <paramref name="height"/>; it is filled whole.</param>
    public static void Render(byte[] source, int sourceWidth, int sourceHeight, Int32Rect area, byte[] target, int width, int height, ZoomView view)
    {
        int sourceStride = sourceWidth * 4;
        int stride = width * 4;
        if (view.IsFull && area.Width == width && area.Height == height)
        {
            // Pixel for pixel: the rows of the rectangle as they are.
            if (area.X == 0 && sourceWidth == width && sourceHeight >= height)
            {
                Buffer.BlockCopy(source, area.Y * sourceStride, target, 0, stride * height);
                return;
            }
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(source, (area.Y + y) * sourceStride + area.X * 4, target, y * stride, stride);
            }
            return;
        }

        int minX = area.X, maxX = area.X + area.Width - 1;
        int minY = area.Y, maxY = area.Y + area.Height - 1;
        double stepX = view.Width * area.Width / width; // how much of a source pixel each pixel of the result covers
        double stepY = view.Height * area.Height / height;

        // The source pixels on both sides of where each column's centre lands, and how far towards the right one it is (in 256ths).
        var lefts = new int[width];
        var rights = new int[width];
        var weightsX = new int[width];
        for (int x = 0; x < width; x++)
        {
            double sx = Math.Clamp(area.X + view.X * area.Width + (x + 0.5) * stepX - 0.5, minX, maxX);
            int left = (int)sx;
            lefts[x] = left * 4;
            rights[x] = Math.Min(left + 1, maxX) * 4;
            weightsX[x] = (int)((sx - left) * 256);
        }

        Parallel.For(0, height, y =>
        {
            double sy = Math.Clamp(area.Y + view.Y * area.Height + (y + 0.5) * stepY - 0.5, minY, maxY);
            int top = (int)sy;
            int wy = (int)((sy - top) * 256);
            int topRow = top * sourceStride;
            int bottomRow = Math.Min(top + 1, maxY) * sourceStride;
            int outRow = y * stride;

            for (int x = 0; x < width; x++)
            {
                int left = lefts[x];
                int right = rights[x];
                int wx = weightsX[x];
                int o = outRow + x * 4;
                for (int c = 0; c < 3; c++)
                {
                    int upper = source[topRow + left + c] * (256 - wx) + source[topRow + right + c] * wx;
                    int lower = source[bottomRow + left + c] * (256 - wx) + source[bottomRow + right + c] * wx;
                    target[o + c] = (byte)((upper * (256 - wy) + lower * wy + (1 << 15)) >> 16);
                }
                target[o + 3] = 255;
            }
        });
    }
}
