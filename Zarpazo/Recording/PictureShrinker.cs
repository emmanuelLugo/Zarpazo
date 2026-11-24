namespace Zarpazo.Recording;

/// <summary>
/// Makes a picture smaller, quickly and on all cores: a recorded area wider than <see cref="VideoJob.MaxSide"/>, or a saved
/// video at a lower size. Each pixel is the average of the source pixels it covers, the ones it only partly covers counted
/// by how much: a bilinear sample (the four nearest pixels) skipped detail when shrinking by less than half, and thin text
/// came out jagged and blurred. GDI+'s high quality resampling took about 180 ms for a full HD picture, longer than a frame.
/// </summary>
internal static class PictureShrinker
{
    /// <param name="source">BGRA, <paramref name="sourceWidth"/> pixels a row, top row first.</param>
    /// <param name="target">BGRA, tightly packed, <paramref name="width"/> x <paramref name="height"/>.</param>
    public static void Shrink(byte[] source, int sourceWidth, int sourceHeight, byte[] target, int width, int height)
    {
        int block = sourceWidth / width;
        if (block >= 2 && sourceWidth == width * block && sourceHeight == height * block)
        {
            // Exactly a half, a third...: the same averages, worked out faster (a 4K screen recorded at full HD, live).
            Average(source, sourceWidth, sourceHeight, target, width, height, block, block, block);
            return;
        }

        (int[] lefts, int[][] acrossWeights) = Spans(sourceWidth, width);
        (int[] tops, int[][] downWeights) = Spans(sourceHeight, height);
        int stride = sourceWidth * 4;

        // Each row of the result: first the source rows it covers are mixed into one line, then that line is mixed across.
        Parallel.For(0, height, y =>
        {
            Span<int> line = sourceWidth <= 8192 ? stackalloc int[sourceWidth * 3] : new int[sourceWidth * 3];
            line.Clear();
            int[] down = downWeights[y];
            for (int k = 0; k < down.Length; k++)
            {
                int weight = down[k];
                int i = (tops[y] + k) * stride;
                for (int x = 0, l = 0; x < sourceWidth; x++, i += 4, l += 3)
                {
                    line[l] += source[i] * weight;
                    line[l + 1] += source[i + 1] * weight;
                    line[l + 2] += source[i + 2] * weight;
                }
            }

            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                int[] across = acrossWeights[x];
                int b = 0, g = 0, r = 0;
                for (int k = 0, l = lefts[x] * 3; k < across.Length; k++, l += 3)
                {
                    b += line[l] * across[k];
                    g += line[l + 1] * across[k];
                    r += line[l + 2] * across[k];
                }
                // Both sets of weights add up to 256.
                target[o] = (byte)((b + (1 << 15)) >> 16);
                target[o + 1] = (byte)((g + (1 << 15)) >> 16);
                target[o + 2] = (byte)((r + (1 << 15)) >> 16);
                target[o + 3] = 255;
            }
        });
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

    // For each pixel of the result along one side: the first source pixel it covers, and how much of it and of the next
    // ones, in 256ths that add up to 256.
    private static (int[] First, int[][] Weights) Spans(int sourceSize, int size)
    {
        double step = (double)sourceSize / size;
        var first = new int[size];
        var weights = new int[size][];
        for (int i = 0; i < size; i++)
        {
            double start = i * step, end = Math.Min(sourceSize, (i + 1) * step);
            int from = Math.Min((int)start, sourceSize - 1);
            int to = Math.Clamp((int)Math.Ceiling(end) - 1, from, sourceSize - 1);
            var weight = new int[to - from + 1];
            int total = 0, largest = 0;
            for (int k = 0; k < weight.Length; k++)
            {
                double cover = Math.Min(end, from + k + 1) - Math.Max(start, from + k);
                weight[k] = (int)Math.Round(Math.Max(0, cover) / step * 256);
                total += weight[k];
                largest = weight[k] > weight[largest] ? k : largest;
            }
            weight[largest] += 256 - total; // what rounding lost or added
            first[i] = from;
            weights[i] = weight;
        }
        return (first, weights);
    }
}
