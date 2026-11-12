namespace Zarpazo.Recording;

/// <summary>Makes one picture of the zoomed video: the part of the recorded picture the camera shows, stretched smoothly (bilinear) to the full size.</summary>
internal static class ZoomResampler
{
    /// <param name="source">BGRA, width x height, top row first.</param>
    /// <param name="target">The same size as the source; it is filled whole.</param>
    public static void Render(byte[] source, byte[] target, int width, int height, ZoomView view)
    {
        if (view.IsFull)
        {
            Buffer.BlockCopy(source, 0, target, 0, width * height * 4);
            return;
        }

        int stride = width * 4;
        int maxX = width - 1;
        int maxY = height - 1;

        // The picture is as big as the source, so each of its pixels covers view.Width of a source pixel. The source pixels
        // on both sides of where each column's centre lands, and how far towards the right one it is (in 256ths).
        var lefts = new int[width];
        var rights = new int[width];
        var weightsX = new int[width];
        for (int x = 0; x < width; x++)
        {
            double sx = Math.Clamp(view.X * width + (x + 0.5) * view.Width - 0.5, 0, maxX);
            int left = (int)sx;
            lefts[x] = left * 4;
            rights[x] = Math.Min(left + 1, maxX) * 4;
            weightsX[x] = (int)((sx - left) * 256);
        }

        Parallel.For(0, height, y =>
        {
            double sy = Math.Clamp(view.Y * height + (y + 0.5) * view.Height - 0.5, 0, maxY);
            int top = (int)sy;
            int wy = (int)((sy - top) * 256);
            int topRow = top * stride;
            int bottomRow = Math.Min(top + 1, maxY) * stride;
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
