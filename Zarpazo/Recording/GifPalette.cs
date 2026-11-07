namespace Zarpazo.Recording;

/// <summary>
/// A color table of at most 256 entries for a set of pixels, made by median cut: the colors are split into boxes of similar
/// ones and each box becomes one entry, the average of the pixels in it (so a flat interface color comes out exact).
/// </summary>
internal sealed class GifPalette
{
    private const int Bins = 1 << 15; // colors are grouped by their top 5 bits per channel to keep this fast

    private readonly int[] _binToIndex;
    private readonly int[] _nearest = new int[Bins]; // for each color bin, the closest entry, worked out when first asked

    private GifPalette(byte[] rgb, int[] binToIndex)
    {
        Rgb = rgb;
        _binToIndex = binToIndex;
        Array.Fill(_nearest, -1);
    }

    /// <summary>Red, green, blue for each entry, one after the other.</summary>
    public byte[] Rgb { get; }

    public int Count => Rgb.Length / 3;

    public int IndexOf(byte red, byte green, byte blue) => _binToIndex[Bin(red, green, blue)];

    /// <summary>The entry closest to a color that need not be in the picture (dithering asks for colors that were adjusted).</summary>
    public int Nearest(int red, int green, int blue)
    {
        int bin = Bin(Math.Clamp(red, 0, 255), Math.Clamp(green, 0, 255), Math.Clamp(blue, 0, 255));
        if (_nearest[bin] >= 0)
        {
            return _nearest[bin];
        }

        // Measured from the middle of the bin so every color in it gets the same answer and the search is done once per bin.
        int r = Channel(bin, 0) * 8 + 4;
        int g = Channel(bin, 1) * 8 + 4;
        int b = Channel(bin, 2) * 8 + 4;
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            int dr = Rgb[i * 3] - r;
            int dg = Rgb[i * 3 + 1] - g;
            int db = Rgb[i * 3 + 2] - b;
            int distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return _nearest[bin] = best;
    }

    private static int Bin(int red, int green, int blue) => ((red >> 3) << 10) | ((green >> 3) << 5) | (blue >> 3);

    private static int Channel(int bin, int channel) => (bin >> (10 - 5 * channel)) & 31; // 0 red, 1 green, 2 blue

    /// <param name="skip">Pixels (one flag per pixel) to leave out, such as the ones left transparent; null to use them all.</param>
    public static GifPalette Build(byte[] bgra, bool[]? skip, int maxColors)
    {
        var count = new int[Bins];
        var sum = new long[Bins * 3];
        for (int pixel = 0; pixel < bgra.Length / 4; pixel++)
        {
            if (skip != null && skip[pixel])
            {
                continue;
            }

            int blue = bgra[pixel * 4];
            int green = bgra[pixel * 4 + 1];
            int red = bgra[pixel * 4 + 2];
            int bin = Bin(red, green, blue);
            count[bin]++;
            sum[bin * 3] += red;
            sum[bin * 3 + 1] += green;
            sum[bin * 3 + 2] += blue;
        }

        int[] order = Enumerable.Range(0, Bins).Where(bin => count[bin] > 0).ToArray();
        if (order.Length == 0)
        {
            return new GifPalette([0, 0, 0], new int[Bins]); // nothing to show: one black entry keeps the table valid
        }

        var boxes = new List<Box> { new(0, order.Length, order.Sum(bin => (long)count[bin])) };
        while (boxes.Count < maxColors)
        {
            int best = -1;
            double bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Length < 2)
                {
                    continue;
                }
                // Many pixels and a wide spread of colors: the box where a split changes the picture most.
                double score = boxes[i].Pixels * (Range(order, boxes[i], out _) + 1.0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best < 0)
            {
                break;
            }

            (Box first, Box second) = Split(order, boxes[best], count);
            boxes[best] = first;
            boxes.Add(second);
        }

        var rgb = new byte[boxes.Count * 3];
        var binToIndex = new int[Bins];
        for (int i = 0; i < boxes.Count; i++)
        {
            long r = 0, g = 0, b = 0;
            for (int j = boxes[i].Start; j < boxes[i].Start + boxes[i].Length; j++)
            {
                int bin = order[j];
                r += sum[bin * 3];
                g += sum[bin * 3 + 1];
                b += sum[bin * 3 + 2];
                binToIndex[bin] = i;
            }
            rgb[i * 3] = (byte)(r / boxes[i].Pixels);
            rgb[i * 3 + 1] = (byte)(g / boxes[i].Pixels);
            rgb[i * 3 + 2] = (byte)(b / boxes[i].Pixels);
        }
        return new GifPalette(rgb, binToIndex);
    }

    private readonly record struct Box(int Start, int Length, long Pixels);

    // How wide the colors in the box spread on their widest channel, and which channel that is.
    private static int Range(int[] order, Box box, out int widest)
    {
        int bestRange = -1;
        widest = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            int min = 31;
            int max = 0;
            for (int i = box.Start; i < box.Start + box.Length; i++)
            {
                int value = Channel(order[i], channel);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            if (max - min > bestRange)
            {
                bestRange = max - min;
                widest = channel;
            }
        }
        return bestRange;
    }

    private static (Box, Box) Split(int[] order, Box box, int[] count)
    {
        Range(order, box, out int channel);
        Array.Sort(order, box.Start, box.Length, Comparer<int>.Create((a, b) => Channel(a, channel).CompareTo(Channel(b, channel))));

        // Cut where half the pixels are on each side, leaving at least one color in each.
        long half = box.Pixels / 2;
        long seen = 0;
        int cut = box.Start + 1;
        for (int i = box.Start; i < box.Start + box.Length - 1; i++)
        {
            seen += count[order[i]];
            cut = i + 1;
            if (seen >= half)
            {
                break;
            }
        }

        long firstPixels = 0;
        for (int i = box.Start; i < cut; i++)
        {
            firstPixels += count[order[i]];
        }
        return (new Box(box.Start, cut - box.Start, firstPixels), new Box(cut, box.Start + box.Length - cut, box.Pixels - firstPixels));
    }
}
