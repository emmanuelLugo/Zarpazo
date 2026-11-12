namespace Zarpazo.Recording;

/// <summary>
/// Makes a recorded picture smaller (an area wider than <see cref="VideoJob.MaxSide"/>), quickly and on all cores. Shrunk
/// by half or more (a 4K screen), each pixel is the average of the block of pixels it covers; by less (a 2K screen), one
/// smooth (bilinear) sample. GDI+'s high quality resampling took about 180 ms for a full HD picture, longer than a frame.
/// </summary>
internal static class PictureShrinker
{
    /// <param name="source">BGRA, <paramref name="sourceWidth"/> pixels a row, top row first.</param>
    /// <param name="target">BGRA, tightly packed, <paramref name="width"/> x <paramref name="height"/>.</param>
    public static void Shrink(byte[] source, int sourceWidth, int sourceHeight, byte[] target, int width, int height)
    {
        double stepX = (double)sourceWidth / width;
        double stepY = (double)sourceHeight / height;
        int block = (int)Math.Floor(Math.Min(stepX, stepY));
        if (block >= 2)
        {
            Average(source, sourceWidth, sourceHeight, target, width, height, stepX, stepY, block);
        }
        else
        {
            Bilinear(source, sourceWidth, sourceHeight, target, width, height, stepX, stepY);
        }
    }

    private static void Average(byte[] source, int sourceWidth, int sourceHeight, byte[] target, int width, int height,
        double stepX, double stepY, int block)
    {
        int stride = sourceWidth * 4;
        int count = block * block;
        var lefts = new int[width];
        for (int x = 0; x < width; x++)
        {
            lefts[x] = Math.Min((int)(x * stepX), sourceWidth - block) * 4;
        }

        Parallel.For(0, height, y =>
        {
            int top = Math.Min((int)(y * stepY), sourceHeight - block);
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                int b = 0, g = 0, r = 0;
                for (int dy = 0; dy < block; dy++)
                {
                    int i = (top + dy) * stride + lefts[x];
                    for (int dx = 0; dx < block; dx++, i += 4)
                    {
                        b += source[i];
                        g += source[i + 1];
                        r += source[i + 2];
                    }
                }
                target[o] = (byte)(b / count);
                target[o + 1] = (byte)(g / count);
                target[o + 2] = (byte)(r / count);
                target[o + 3] = 255;
            }
        });
    }

    private static void Bilinear(byte[] source, int sourceWidth, int sourceHeight, byte[] target, int width, int height,
        double stepX, double stepY)
    {
        int stride = sourceWidth * 4;
        int maxX = sourceWidth - 1;
        int maxY = sourceHeight - 1;
        var lefts = new int[width];
        var rights = new int[width];
        var weights = new int[width];
        for (int x = 0; x < width; x++)
        {
            double sx = Math.Clamp((x + 0.5) * stepX - 0.5, 0, maxX);
            int left = (int)sx;
            lefts[x] = left * 4;
            rights[x] = Math.Min(left + 1, maxX) * 4;
            weights[x] = (int)((sx - left) * 256);
        }

        Parallel.For(0, height, y =>
        {
            double sy = Math.Clamp((y + 0.5) * stepY - 0.5, 0, maxY);
            int top = (int)sy;
            int wy = (int)((sy - top) * 256);
            int upperRow = top * stride;
            int lowerRow = Math.Min(top + 1, maxY) * stride;
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                int a = lefts[x], b = rights[x], wx = weights[x];
                for (int c = 0; c < 3; c++)
                {
                    int upper = source[upperRow + a + c] * (256 - wx) + source[upperRow + b + c] * wx;
                    int lower = source[lowerRow + a + c] * (256 - wx) + source[lowerRow + b + c] * wx;
                    target[o + c] = (byte)((upper * (256 - wy) + lower * wy + (1 << 15)) >> 16);
                }
                target[o + 3] = 255;
            }
        });
    }
}
