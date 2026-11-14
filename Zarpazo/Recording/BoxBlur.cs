namespace Zarpazo.Recording;

/// <summary>Blurs a rectangle of a BGRA picture so strongly that what was written in it cannot be read.</summary>
internal static class BoxBlur
{
    private const int Passes = 3; // three box blurs in a row come out as a smooth Gaussian one

    /// <param name="picture">BGRA, <paramref name="width"/> pixels wide; changed in place.</param>
    /// <param name="area">The rectangle to blur, inside the picture. What is outside it is not read, so it cannot bleed in.</param>
    /// <param name="radius">How far each pixel is spread, in pixels.</param>
    public static void Apply(byte[] picture, int width, System.Windows.Int32Rect area, int radius)
    {
        if (area.Width < 1 || area.Height < 1 || radius < 1)
        {
            return;
        }
        radius = Math.Min(radius, Math.Max(area.Width, area.Height));

        var line = new byte[Math.Max(area.Width, area.Height) * 3];
        for (int pass = 0; pass < Passes; pass++)
        {
            for (int y = area.Y; y < area.Y + area.Height; y++)
            {
                Blur(picture, (y * width + area.X) * 4, 4, area.Width, radius, line);
            }
            for (int x = area.X; x < area.X + area.Width; x++)
            {
                Blur(picture, (area.Y * width + x) * 4, width * 4, area.Height, radius, line);
            }
        }
    }

    // One row or column of `count` pixels, `step` bytes apart, as a sliding sum; beyond its ends the edge pixel is repeated.
    private static void Blur(byte[] picture, int first, int step, int count, int radius, byte[] line)
    {
        for (int i = 0; i < count; i++)
        {
            int at = first + i * step;
            line[i * 3] = picture[at];
            line[i * 3 + 1] = picture[at + 1];
            line[i * 3 + 2] = picture[at + 2];
        }

        int window = radius * 2 + 1;
        for (int channel = 0; channel < 3; channel++)
        {
            long sum = 0;
            for (int i = -radius; i <= radius; i++)
            {
                sum += line[Math.Clamp(i, 0, count - 1) * 3 + channel];
            }
            for (int i = 0; i < count; i++)
            {
                picture[first + i * step + channel] = (byte)(sum / window);
                sum += line[Math.Min(i + radius + 1, count - 1) * 3 + channel] - line[Math.Max(i - radius, 0) * 3 + channel];
            }
        }
    }
}
