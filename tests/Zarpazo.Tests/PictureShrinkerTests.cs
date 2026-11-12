using System.Diagnostics;
using System.Drawing;
using Zarpazo.Recording;
using Xunit.Abstractions;

namespace Zarpazo.Tests;

[Collection("Recording")]
public sealed class PictureShrinkerTests(ITestOutputHelper output)
{
    [Fact]
    public void Halving_averages_each_square_of_four_pixels()
    {
        // 4x2: black and white columns; halved, every pixel is half grey.
        byte[] source = new byte[4 * 2 * 4];
        for (int i = 0; i < 8; i++)
        {
            byte value = (byte)(i % 2 == 0 ? 0 : 255);
            (source[i * 4], source[i * 4 + 1], source[i * 4 + 2], source[i * 4 + 3]) = (value, value, value, 255);
        }
        var target = new byte[2 * 1 * 4];

        PictureShrinker.Shrink(source, 4, 2, target, 2, 1);

        Assert.InRange(target[0], 120, 135);
        Assert.InRange(target[4], 120, 135);
        Assert.Equal(255, target[3]);
    }

    [Fact]
    public void A_4k_picture_shrinks_to_full_hd_well_within_a_frame()
    {
        var source = new byte[3840 * 2160 * 4];
        new Random(1).NextBytes(source);
        var target = new byte[1920 * 1080 * 4];
        PictureShrinker.Shrink(source, 3840, 2160, target, 1920, 1080);

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            PictureShrinker.Shrink(source, 3840, 2160, target, 1920, 1080);
        }
        double ms = watch.Elapsed.TotalMilliseconds / 5;
        output.WriteLine($"4K to full HD: {ms:0.0} ms");
        Assert.True(ms < 120, $"{ms:0.0} ms"); // generous: a debug build, with other tests running at the same time
    }

    [Fact]
    public void A_2k_picture_shrinks_to_full_hd_well_within_a_frame()
    {
        var source = new byte[2560 * 1440 * 4];
        new Random(1).NextBytes(source);
        var target = new byte[1920 * 1080 * 4];
        PictureShrinker.Shrink(source, 2560, 1440, target, 1920, 1080);

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            PictureShrinker.Shrink(source, 2560, 1440, target, 1920, 1080);
        }
        double ms = watch.Elapsed.TotalMilliseconds / 5;
        output.WriteLine($"2K to full HD: {ms:0.0} ms");
        Assert.True(ms < 120, $"{ms:0.0} ms");
    }

    [NeedsGpuFact]
    public void An_area_one_pixel_too_wide_for_the_video_is_cropped_not_resampled()
    {
        var area = new Rectangle(0, 0, 1919, 1079);
        (int width, int height) = RegionGrabber.FitSize(area, VideoJob.MaxSide, even: true);
        using var grabber = new RegionGrabber(area, width, height);
        var buffer = new byte[width * height * 4];
        grabber.Grab(buffer);

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            grabber.Grab(buffer);
        }
        double ms = watch.Elapsed.TotalMilliseconds / 10;
        output.WriteLine($"1919x1079 into 1918x1078: {ms:0.0} ms a picture");
        Assert.True(ms < 60, $"{ms:0.0} ms a picture: it was 177 ms when the picture was resampled");
    }
}
