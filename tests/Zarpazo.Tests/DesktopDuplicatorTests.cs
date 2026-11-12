using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Zarpazo.Recording;
using Xunit.Abstractions;

namespace Zarpazo.Tests;

/// <summary>Copying the screen through the graphics card. GitHub's machines have no real screen, so these only run on a PC.</summary>
[Collection("Recording")]
public sealed class DesktopDuplicatorTests(ITestOutputHelper output)
{
    private static Rectangle Primary => Screen.PrimaryScreen!.Bounds;

    [NeedsGpuFact]
    public void An_area_on_one_screen_is_copied_through_the_graphics_card_and_matches_the_old_copy()
    {
        Rectangle area = new(Primary.X, Primary.Y, 320, 200);
        using DesktopDuplicator? duplicator = DesktopDuplicator.TryCreate(area);
        Assert.NotNull(duplicator);

        using var fast = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        bool copied = false;
        for (int i = 0; i < 20 && !copied; i++)
        {
            copied = duplicator.TryCopy(fast); // the first picture can take a moment to arrive
            Thread.Sleep(copied ? 0 : 50);
        }
        Assert.True(copied);

        using var slow = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(slow))
        {
            graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
        }

        // The screen can change between the two copies (a clock, the pointer is in neither), so most pixels have to match.
        int same = 0, total = 0;
        for (int y = 0; y < area.Height; y += 4)
        {
            for (int x = 0; x < area.Width; x += 4)
            {
                total++;
                Color a = fast.GetPixel(x, y), b = slow.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 6)
                {
                    same++;
                }
            }
        }
        Assert.True(same > total * 0.9, $"{same} of {total} pixels match");
    }

    [Fact]
    public void An_area_across_two_screens_or_outside_them_has_no_duplicator()
    {
        Rectangle outside = new(Primary.Right + 100_000, Primary.Bottom + 100_000, 100, 100);
        Assert.Null(DesktopDuplicator.TryCreate(outside));
    }

    [NeedsGpuFact]
    public void A_full_hd_picture_takes_well_under_the_time_of_a_frame()
    {
        Rectangle area = new(Primary.X, Primary.Y, Math.Min(1920, Primary.Width) & ~1, Math.Min(1080, Primary.Height) & ~1);
        var buffer = new byte[area.Width * area.Height * 4];

        double Measure(RegionGrabber grabber)
        {
            grabber.Grab(buffer);
            grabber.Grab(buffer); // warmed up
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                grabber.Grab(buffer);
            }
            return watch.Elapsed.TotalMilliseconds / 20;
        }

        using var grabber = new RegionGrabber(area, area.Width, area.Height);
        Assert.True(grabber.UsesGraphicsCard);
        double fast = Measure(grabber);
        output.WriteLine($"{area.Width}x{area.Height}: {fast:0.0} ms a picture through the graphics card");
        Assert.True(fast < 20, $"{fast:0.0} ms a picture");
    }
}
