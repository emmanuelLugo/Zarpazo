namespace Zarpazo.Recording;

/// <summary>
/// The smear a camera leaves on what moves while its shutter is open: the picture, or the pointer, averaged over the places
/// it was in during a short moment. Still things come out exactly as they were.
/// </summary>
internal static class MotionBlur
{
    /// <summary>How long the imagined shutter stays open. In seconds, so a video at 60 pictures a second smears as one at 30.</summary>
    public const double ShutterSeconds = 1.0 / 50;

    /// <summary>The pointer's is half as long: it is small and moves far faster than the camera, and with the full one it
    /// became a streak that no longer looked like a pointer.</summary>
    public const double PointerShutterSeconds = ShutterSeconds / 2;

    // Places averaged are about this many pixels apart: further, and copies show instead of a smear. Past the most places
    // (a very fast move) they spread out.
    private const double Spacing = 2.5;
    private const int MaxSamples = 24;

    /// <summary>How many places to average for something that moves <paramref name="pixels"/> during the shutter; 1 leaves it sharp.</summary>
    public static int Samples(double pixels) => pixels < Spacing ? 1 : Math.Min(MaxSamples, (int)Math.Ceiling(pixels / Spacing) + 1);

    /// <summary>How far, in pixels of a picture of this size, the corners of what the camera shows move from one view to the other.</summary>
    public static double Travel(ZoomView from, ZoomView to, int width, int height)
    {
        double left = Math.Abs(to.X - from.X), right = Math.Abs(to.X + to.Width - from.X - from.Width);
        double top = Math.Abs(to.Y - from.Y), bottom = Math.Abs(to.Y + to.Height - from.Y - from.Height);
        double size = Math.Min(from.Width, to.Width);
        return Math.Max(Math.Max(left, right) * width, Math.Max(top, bottom) * height) / size;
    }

    /// <summary>
    /// Smears a picture taken with the camera at <paramref name="at"/> as it moves from <paramref name="from"/> to
    /// <paramref name="to"/>: each pixel averages where the picture's pixels would be for the views in between.
    /// </summary>
    /// <param name="sharp">BGRA, <paramref name="width"/> by <paramref name="height"/>.</param>
    /// <param name="target">The same size, another array; it is filled whole.</param>
    public static void Camera(byte[] sharp, byte[] target, int width, int height, ZoomView from, ZoomView at, ZoomView to, int samples)
    {
        // Views only slide and grow, so the column a pixel takes does not depend on its row: worked out once.
        var columns = new int[samples][];
        var shares = new double[samples];
        for (int s = 0; s < samples; s++)
        {
            double u = shares[s] = samples == 1 ? 0.5 : s / (samples - 1.0);
            double left = from.X + (to.X - from.X) * u, size = from.Width + (to.Width - from.Width) * u;
            columns[s] = new int[width];
            for (int x = 0; x < width; x++)
            {
                double taken = (left + (x + 0.5) / width * size - at.X) / at.Width * width;
                columns[s][x] = Math.Clamp((int)taken, 0, width - 1) * 4;
            }
        }

        int stride = width * 4;
        Parallel.For(0, height, y =>
        {
            Span<int> rows = stackalloc int[MaxSamples];
            for (int s = 0; s < samples; s++)
            {
                double top = from.Y + (to.Y - from.Y) * shares[s], size = from.Height + (to.Height - from.Height) * shares[s];
                double taken = (top + (y + 0.5) / height * size - at.Y) / at.Height * height;
                rows[s] = Math.Clamp((int)taken, 0, height - 1) * stride;
            }
            for (int x = 0, o = y * stride; x < width; x++, o += 4)
            {
                int blue = 0, green = 0, red = 0;
                for (int s = 0; s < samples; s++)
                {
                    int i = rows[s] + columns[s][x];
                    blue += sharp[i];
                    green += sharp[i + 1];
                    red += sharp[i + 2];
                }
                target[o] = (byte)((blue + samples / 2) / samples);
                target[o + 1] = (byte)((green + samples / 2) / samples);
                target[o + 2] = (byte)((red + samples / 2) / samples);
                target[o + 3] = 255;
            }
        });
    }

    /// <summary>
    /// Draws a small picture over another, smeared along a straight line: averaged over the places between the two offsets.
    /// </summary>
    /// <param name="picture">BGRA, <paramref name="width"/> by <paramref name="height"/>.</param>
    /// <param name="sprite">BGRA with the colours already multiplied by their opacity, <paramref name="size"/> by <paramref name="size"/>.</param>
    /// <param name="left">Where the sprite's corner goes at the middle of the move.</param>
    /// <param name="from">Where it starts, from there; <paramref name="to"/>, where it ends.</param>
    public static void Sprite(byte[] picture, int width, int height, byte[] sprite, int size, int left, int top,
        (double X, double Y) from, (double X, double Y) to, int samples)
    {
        var offsets = new (int X, int Y)[samples];
        for (int s = 0; s < samples; s++)
        {
            double u = samples == 1 ? 0.5 : s / (samples - 1.0);
            offsets[s] = ((int)Math.Round(from.X + (to.X - from.X) * u), (int)Math.Round(from.Y + (to.Y - from.Y) * u));
        }

        // The part of the picture the smear touches.
        int x0 = Math.Max(0, left + offsets.Min(o => o.X)), x1 = Math.Min(width, left + offsets.Max(o => o.X) + size);
        int y0 = Math.Max(0, top + offsets.Min(o => o.Y)), y1 = Math.Min(height, top + offsets.Max(o => o.Y) + size);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int blue = 0, green = 0, red = 0, alpha = 0;
                foreach ((int ox, int oy) in offsets)
                {
                    int sx = x - left - ox, sy = y - top - oy;
                    if ((uint)sx >= (uint)size || (uint)sy >= (uint)size)
                    {
                        continue;
                    }
                    int i = (sy * size + sx) * 4;
                    blue += sprite[i];
                    green += sprite[i + 1];
                    red += sprite[i + 2];
                    alpha += sprite[i + 3];
                }
                if (alpha == 0)
                {
                    continue;
                }
                int p = (y * width + x) * 4;
                int through = 255 * samples - alpha; // how much of what is under shows, out of 255 * samples
                picture[p] = (byte)Math.Min(255, (blue * 255 + picture[p] * through) / (255 * samples));
                picture[p + 1] = (byte)Math.Min(255, (green * 255 + picture[p + 1] * through) / (255 * samples));
                picture[p + 2] = (byte)Math.Min(255, (red * 255 + picture[p + 2] * through) / (255 * samples));
            }
        }
    }
}
