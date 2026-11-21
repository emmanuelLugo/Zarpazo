using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.Tests;

public sealed class ImageResizerTests
{
    private static BitmapSource Solid(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)200);
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    [Fact]
    public void Full_size_returns_the_same_picture()
    {
        BitmapSource image = Solid(100, 50);
        Assert.Same(image, ImageResizer.Scale(image, 1));
    }

    [Theory]
    [InlineData(0.75, 75, 38)]
    [InlineData(0.5, 50, 25)]
    [InlineData(0.25, 25, 12)]
    public void Scales_both_sides(double factor, int width, int height)
    {
        BitmapSource result = ImageResizer.Scale(Solid(100, 50), factor);
        Assert.Equal(width, result.PixelWidth);
        Assert.Equal(height, result.PixelHeight);
    }

    [Fact]
    public void Never_collapses_to_nothing()
    {
        BitmapSource result = ImageResizer.Scale(Solid(2, 2), 0.25);
        Assert.Equal(1, result.PixelWidth);
        Assert.Equal(1, result.PixelHeight);
    }
}
