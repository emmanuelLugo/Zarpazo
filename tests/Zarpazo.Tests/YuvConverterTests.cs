using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>The NV12 to BGRA conversion: four pixels at a time, with the last few of a row done one by one.</summary>
public sealed class YuvConverterTests
{
    // 8 and 12 are whole groups of four; 9, 11 and 6 end with a few pixels on their own.
    [Theory]
    [InlineData(8, true, false)]
    [InlineData(12, false, false)]
    [InlineData(9, true, true)]
    [InlineData(11, false, true)]
    [InlineData(6, true, false)]
    public void Every_pixel_has_the_colour_the_standard_formula_gives_whether_it_is_in_a_group_of_four_or_alone(int width, bool hd, bool full)
    {
        const int Height = 6;
        int pitch = width + 6; // the decoder's rows are wider than the picture
        int chromaStart = pitch * Height;
        var nv12 = new byte[pitch * Height * 3 / 2];
        new Random(width).NextBytes(nv12);
        var bgra = new byte[width * Height * 4];

        new YuvConverter(hd, full).ToBgra(nv12, pitch, chromaStart, bgra, width, Height);

        (double kr, double kb) = hd ? (0.2126, 0.0722) : (0.299, 0.114);
        double kg = 1 - kr - kb;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double luma = full ? nv12[y * pitch + x] : (nv12[y * pitch + x] - 16) * 255.0 / 219;
                double scale = full ? 1 : 255.0 / 224;
                double u = (nv12[chromaStart + y / 2 * pitch + (x & ~1)] - 128) * scale;
                double v = (nv12[chromaStart + y / 2 * pitch + (x & ~1) + 1] - 128) * scale;
                double red = luma + 2 * (1 - kr) * v;
                double blue = luma + 2 * (1 - kb) * u;
                double green = (luma - kr * red - kb * blue) / kg;
                int i = (y * width + x) * 4;
                Assert.InRange(bgra[i], Math.Clamp(blue, 0, 255) - 1.5, Math.Clamp(blue, 0, 255) + 1.5);
                Assert.InRange(bgra[i + 1], Math.Clamp(green, 0, 255) - 1.5, Math.Clamp(green, 0, 255) + 1.5);
                Assert.InRange(bgra[i + 2], Math.Clamp(red, 0, 255) - 1.5, Math.Clamp(red, 0, 255) + 1.5);
                Assert.Equal(255, bgra[i + 3]);
            }
        }
    }
}
