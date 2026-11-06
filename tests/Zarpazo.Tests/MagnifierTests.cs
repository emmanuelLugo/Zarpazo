using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Overlay;

namespace Zarpazo.Tests;

public class MagnifierTests
{
    private const int Size = 20;

    // Every pixel has its own color: blue = 10 * x, green = 10 * y, red = 100.
    private static BitmapSource Gradient()
    {
        var pixels = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size + x) * 4;
                pixels[i] = (byte)(10 * x);
                pixels[i + 1] = (byte)(10 * y);
                pixels[i + 2] = 100;
            }
        }
        return BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgr32, null, pixels, Size * 4);
    }

    [Fact]
    public void Reads_the_color_of_the_pixel_under_the_cursor()
    {
        Sta.Run(() =>
        {
            var magnifier = new Magnifier();

            magnifier.Show(Gradient(), 3, 4);

            Assert.Equal(Color.FromRgb(100, 40, 30), magnifier.Current);
            Assert.Equal("#64281E", magnifier.Text);
        });
    }

    [Fact]
    public void Toggles_between_hex_and_rgb()
    {
        Sta.Run(() =>
        {
            var magnifier = new Magnifier();
            magnifier.Show(Gradient(), 3, 4);

            magnifier.ToggleFormat();
            Assert.Equal("rgb(100, 40, 30)", magnifier.Text);

            magnifier.ToggleFormat();
            Assert.Equal("#64281E", magnifier.Text);
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Size - 1, Size - 1)]
    [InlineData(0, Size - 1)]
    [InlineData(Size - 1, 0)]
    public void Works_at_the_screen_corners_where_the_view_hangs_past_the_edge(int x, int y)
    {
        Sta.Run(() =>
        {
            var magnifier = new Magnifier();

            magnifier.Show(Gradient(), x, y);

            Assert.Equal(Color.FromRgb(100, (byte)(10 * y), (byte)(10 * x)), magnifier.Current);
        });
    }

    [Fact]
    public void Clamps_a_position_outside_the_image()
    {
        Sta.Run(() =>
        {
            var magnifier = new Magnifier();

            magnifier.Show(Gradient(), -5, 500);

            Assert.Equal(Color.FromRgb(100, (byte)(10 * (Size - 1)), 0), magnifier.Current);
        });
    }
}
