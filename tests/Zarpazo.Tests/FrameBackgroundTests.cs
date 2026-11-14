using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>What can be behind the recording in its frame: a built-in background, a colour or gradient of one's own, or a picture.</summary>
public sealed class FrameBackgroundTests
{
    [Fact]
    public void A_colour_of_ones_own_is_kept_and_read_back()
    {
        string key = FrameBackground.Custom(Color.FromRgb(255, 0, 128), Color.FromRgb(0, 64, 255), 120);

        Assert.Equal("custom:#FF0080:#0040FF:120", key);
        Assert.True(FrameBackground.TryColors(key, out Color from, out Color to, out int angle));
        Assert.Equal((Color.FromRgb(255, 0, 128), Color.FromRgb(0, 64, 255), 120), (from, to, angle));
        Assert.Equal(key, FrameBackground.Tidy(key));
    }

    [Fact]
    public void A_built_in_background_gives_its_colours_so_they_can_be_changed()
    {
        Assert.True(FrameBackground.TryColors("oceano", out Color from, out Color to, out int angle));
        Assert.Equal((Color.FromRgb(0x21, 0x93, 0xB0), Color.FromRgb(0x6D, 0xD5, 0xED), FrameBackground.DefaultAngle), (from, to, angle));
        Assert.False(FrameBackground.TryColors(FrameBackground.Image(@"C:\fondos\mar.jpg"), out _, out _, out _));
    }

    [Fact]
    public void The_angle_is_kept_between_zero_and_a_full_turn()
    {
        Assert.EndsWith(":15", FrameBackground.Custom(Colors.Red, Colors.Blue, 375));
        Assert.EndsWith(":345", FrameBackground.Custom(Colors.Red, Colors.Blue, -15));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-existe")]
    [InlineData("custom:#GGGGGG:#000000:45")]
    [InlineData("custom:#FF0000:#000000")]
    [InlineData("custom:#FF0000:#000000:abc")]
    [InlineData("image:")]
    public void A_background_that_makes_no_sense_becomes_the_default(string? key)
    {
        Assert.Equal(Zarpazo.Output.BeautifyBackground.Default.Key, FrameBackground.Tidy(key));
    }

    [Fact]
    public void A_picture_is_kept_even_if_the_file_is_gone_and_then_painted_as_the_default()
    {
        string key = FrameBackground.Image(@"C:\no\hay\esta.png");

        Assert.Equal(key, FrameBackground.Tidy(key));
        Sta.Run(() => Assert.IsType<LinearGradientBrush>(FrameBackground.ToBrush(key)));
    }

    [Fact]
    public void A_gradient_of_ones_own_runs_edge_to_edge_in_the_slant_chosen()
    {
        Sta.Run(() =>
        {
            var across = (LinearGradientBrush)FrameBackground.ToBrush(FrameBackground.Custom(Colors.Black, Colors.White, 0));
            Assert.Equal((0.0, 0.5, 1.0, 0.5), (across.StartPoint.X, across.StartPoint.Y, across.EndPoint.X, across.EndPoint.Y));

            var corner = (LinearGradientBrush)FrameBackground.ToBrush(FrameBackground.Custom(Colors.Black, Colors.White, 45));
            Assert.Equal((0.0, 0.0, 1.0, 1.0), (corner.StartPoint.X, corner.StartPoint.Y, corner.EndPoint.X, corner.EndPoint.Y));
        });
    }

    [Fact]
    public void A_random_gradient_is_always_a_well_formed_one()
    {
        var random = new Random(7);
        for (int i = 0; i < 50; i++)
        {
            string key = FrameBackground.RandomGradient(random);
            Assert.True(FrameBackground.TryColors(key, out _, out _, out int angle), key);
            Assert.StartsWith("custom:", key);
            Assert.Equal(0, angle % 15);
        }
    }

    [Fact]
    public void The_backdrop_is_drawn_from_a_picture_file()
    {
        string folder = Path.Combine(Path.GetTempPath(), "zarpazo-bg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "green.png");
            var pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (0, 255, 0, 255);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64)));
            using (FileStream file = File.Create(path))
            {
                encoder.Save(file);
            }

            VideoFrameStyle style = (VideoFrameStyle.Default with { Enabled = true, Background = FrameBackground.Image(path), Shadow = 0 }).Tidied();
            byte[] backdrop = FrameBackdrop.Render(style, FrameLayout.For(style, 320, 180));

            Assert.Equal((byte)255, backdrop[1]); // the corner pixel's green: the picture
            Assert.Equal((byte)0, backdrop[2]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Every_look_to_start_from_has_a_name_and_values_in_range()
    {
        Assert.NotEmpty(VideoFrameStyle.Looks);
        foreach ((string name, string tip, VideoFrameStyle style) in VideoFrameStyle.Looks)
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.False(string.IsNullOrWhiteSpace(tip));
            Assert.Equal(style, style.Tidied());
        }
        Assert.Equal(VideoFrameStyle.Looks.Count, VideoFrameStyle.Looks.Select(look => look.Name).Distinct().Count());
    }
}
