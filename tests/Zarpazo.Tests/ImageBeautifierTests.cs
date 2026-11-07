using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.Tests;

public sealed class ImageBeautifierTests
{
    private static BitmapSource Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static Color PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static BeautifyStyle Plain(int padding, int radius = 0) => new("blanco", padding, radius, Shadow: false);

    [Fact]
    public void The_result_is_the_capture_plus_the_padding_on_every_side()
    {
        Sta.Run(() =>
        {
            BitmapSource result = ImageBeautifier.Apply(Solid(40, 30, Colors.Red), Plain(20));

            Assert.Equal((80, 70), (result.PixelWidth, result.PixelHeight));
        });
    }

    [Fact]
    public void The_capture_keeps_its_pixels_in_the_middle_and_the_background_shows_around_it()
    {
        Sta.Run(() =>
        {
            BitmapSource result = ImageBeautifier.Apply(Solid(40, 30, Colors.Red), Plain(20));

            Assert.Equal(Colors.Red, PixelAt(result, 40, 35));          // the middle of the capture
            Assert.Equal(Colors.Red, PixelAt(result, 20, 20));          // its top-left pixel, exactly where the padding ends
            Assert.Equal(Colors.White, PixelAt(result, 5, 5));          // the background
            Assert.Equal(Colors.White, PixelAt(result, 74, 64));
        });
    }

    [Fact]
    public void Rounded_corners_show_the_background_in_the_corner_of_the_capture()
    {
        Sta.Run(() =>
        {
            BitmapSource result = ImageBeautifier.Apply(Solid(60, 60, Colors.Red), Plain(20, radius: 24));

            Assert.Equal(Colors.White, PixelAt(result, 20, 20)); // the corner pixel is cut away
            Assert.Equal(Colors.Red, PixelAt(result, 50, 50));
        });
    }

    [Fact]
    public void A_shadow_darkens_the_background_below_the_capture_but_does_not_change_the_size()
    {
        Sta.Run(() =>
        {
            var style = new BeautifyStyle("blanco", 48, 0, Shadow: true);
            BitmapSource result = ImageBeautifier.Apply(Solid(100, 100, Colors.Red), style);

            Assert.Equal((196, 196), (result.PixelWidth, result.PixelHeight));
            Color below = PixelAt(result, 98, 48 + 100 + 6); // just under the capture
            Assert.True(below.R < 250, $"expected a shadow, got {below}");
            Assert.Equal(Colors.White, PixelAt(result, 2, 2)); // far from the capture, nothing
        });
    }

    [Fact]
    public void A_smaller_scale_gives_a_proportionally_smaller_result_for_previews()
    {
        Sta.Run(() =>
        {
            BitmapSource result = ImageBeautifier.Apply(Solid(200, 100, Colors.Red), Plain(40), scale: 0.5);

            Assert.Equal((140, 90), (result.PixelWidth, result.PixelHeight)); // 100 x 50 plus 20 on each side
        });
    }

    [Fact]
    public void An_unknown_background_key_falls_back_to_the_default()
    {
        Assert.Equal(BeautifyBackground.Default, BeautifyBackground.Find("no-existe"));
        Assert.Equal(BeautifyBackground.Default, BeautifyBackground.Find(null));
        Assert.Equal("oceano", BeautifyBackground.Find("oceano").Key);
    }
}
