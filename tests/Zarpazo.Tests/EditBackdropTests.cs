using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Capture;

namespace Zarpazo.Tests;

public class EditBackdropTests
{
    // Screen from (-10, -5), 20 x 10 pixels, gray. The capture is 4 x 4, red.
    private static readonly Rectangle Bounds = new(-10, -5, 20, 10);

    private static BitmapSource Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2]) = (blue, green, red);
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
    }

    private static (byte Blue, byte Green, byte Red) PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2]);
    }

    [Fact]
    public void The_capture_goes_back_exactly_where_it_was_and_the_rest_stays_as_the_screen()
    {
        Sta.Run(() =>
        {
            (BitmapSource image, System.Drawing.Point origin) =
                EditBackdrop.Place(Solid(20, 10, 0x80, 0x80, 0x80), Bounds, Solid(4, 4, 0, 0, 255), left: -5, top: -2);

            Assert.Equal(new System.Drawing.Point(-5, -2), origin);
            Assert.Equal(PixelFormats.Bgr32, image.Format);
            Assert.Equal((20, 10), (image.PixelWidth, image.PixelHeight));
            // (-5, -2) is pixel (5, 3) of the image.
            Assert.Equal(((byte)0, (byte)0, (byte)255), PixelAt(image, 5, 3));
            Assert.Equal(((byte)0, (byte)0, (byte)255), PixelAt(image, 8, 6));
            Assert.Equal(((byte)0x80, (byte)0x80, (byte)0x80), PixelAt(image, 4, 3));
            Assert.Equal(((byte)0x80, (byte)0x80, (byte)0x80), PixelAt(image, 9, 6));
            Assert.Equal(((byte)0x80, (byte)0x80, (byte)0x80), PixelAt(image, 0, 0));
        });
    }

    [Fact]
    public void A_capture_whose_place_is_off_the_screen_is_moved_to_the_nearest_spot_that_fits()
    {
        Sta.Run(() =>
        {
            (_, System.Drawing.Point farAway) = EditBackdrop.Place(Solid(20, 10, 0, 0, 0), Bounds, Solid(4, 4, 0, 0, 255), left: 5000, top: 5000);
            (_, System.Drawing.Point beforeStart) = EditBackdrop.Place(Solid(20, 10, 0, 0, 0), Bounds, Solid(4, 4, 0, 0, 255), left: -5000, top: -5000);

            Assert.Equal(new System.Drawing.Point(Bounds.Right - 4, Bounds.Bottom - 4), farAway);
            Assert.Equal(new System.Drawing.Point(Bounds.Left, Bounds.Top), beforeStart);
        });
    }
}
