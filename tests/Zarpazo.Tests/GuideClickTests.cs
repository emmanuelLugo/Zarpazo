using System.Drawing;
using Color = System.Windows.Media.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Guide;

namespace Zarpazo.Tests;

public sealed class GuideClickTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private static BitmapSource White(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
    }

    private static Color PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        new CroppedBitmap(image, new System.Windows.Int32Rect(x, y, 1, 1)).CopyPixels(pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void The_marker_rings_the_click_and_leaves_the_rest_of_the_picture_alone()
    {
        Sta.Run(() =>
        {
            BitmapSource marked = ClickMarker.Apply(White(200, 150), new System.Windows.Point(100, 75), 2);

            Assert.Equal((200, 150), (marked.PixelWidth, marked.PixelHeight));
            Color onRing = PixelAt(marked, 100 + 22, 75); // the ring is 22 away from the click
            Assert.True(onRing.R > 200 && onRing.G < 120, $"expected red on the ring, got {onRing}");
            Color far = PixelAt(marked, 190, 140);
            Assert.Equal(Color.FromRgb(255, 255, 255), Color.FromRgb(far.R, far.G, far.B));
        });
    }

    [Fact]
    public void The_marker_works_for_a_click_in_the_corner_of_the_picture()
    {
        Sta.Run(() =>
        {
            BitmapSource marked = ClickMarker.Apply(White(100, 100), new System.Windows.Point(0, 0), 12);

            Assert.Equal((100, 100), (marked.PixelWidth, marked.PixelHeight));
        });
    }

    [Fact]
    public void The_area_is_the_topmost_window_under_the_click()
    {
        Rectangle monitor = new(0, 0, 1920, 1080);
        Rectangle front = new(100, 100, 400, 300);
        Rectangle back = new(0, 0, 1000, 800);

        Rectangle area = ClickRecorder.AreaFor(new Point(150, 150), [front, back], monitor);

        Assert.Equal(front, area);
        Assert.Equal(back, ClickRecorder.AreaFor(new Point(700, 600), [front, back], monitor));
    }

    [Fact]
    public void With_no_window_under_the_click_the_area_is_the_monitor()
    {
        Rectangle monitor = new(0, 0, 1920, 1080);

        Assert.Equal(monitor, ClickRecorder.AreaFor(new Point(1500, 900), [new Rectangle(0, 0, 100, 100)], monitor));
    }

    [Fact]
    public void Copying_the_images_writes_one_file_per_step_named_after_it_and_drops_the_old_ones()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(Path.Combine(_folder, "guide"));
            guide.Add(White(10, 10));
            guide.Add(White(10, 10));
            string share = Path.Combine(_folder, "share");
            Directory.CreateDirectory(share);
            File.WriteAllText(Path.Combine(share, "Paso 9.png"), "left over from an earlier copy");

            List<string> files = GuideShare.WriteFiles(guide.Steps, share);

            Assert.Equal(["Paso 1.png", "Paso 2.png"], files.Select(Path.GetFileName));
            Assert.Equal(["Paso 1.png", "Paso 2.png"], Directory.EnumerateFiles(share).Select(Path.GetFileName).Order());
            Assert.Equal(guide.Steps[0].ReadPng(), File.ReadAllBytes(files[0]));
        });
    }
}
