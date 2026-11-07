using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class GifEncoderTests
{
    private static byte[] Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static byte[] Encode(IReadOnlyList<RecordedFrame> frames, int width, int height, int durationMs)
    {
        using var stream = new MemoryStream();
        GifEncoder.Encode(frames, width, height, durationMs, stream);
        return stream.ToArray();
    }

    private static BitmapDecoder Decode(byte[] gif) =>
        BitmapDecoder.Create(new MemoryStream(gif), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

    private static Color PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static void AssertClose(Color expected, Color actual, int tolerance = 8)
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"expected about {expected}, got {actual}");
    }

    [Fact]
    public void A_single_frame_decodes_to_the_same_size_and_color()
    {
        Sta.Run(() =>
        {
            byte[] gif = Encode([new RecordedFrame(0, 0, 20, 10, Solid(20, 10, Color.FromRgb(30, 144, 255)), 0)], 20, 10, 500);

            BitmapDecoder decoder = Decode(gif);

            Assert.Single(decoder.Frames);
            Assert.Equal((20, 10), (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight));
            AssertClose(Color.FromRgb(30, 144, 255), PixelAt(decoder.Frames[0], 10, 5));
        });
    }

    [Fact]
    public void A_later_frame_is_stored_as_just_its_rectangle_at_its_place()
    {
        Sta.Run(() =>
        {
            var first = new RecordedFrame(0, 0, 20, 10, Solid(20, 10, Colors.Red), 0);
            var second = new RecordedFrame(5, 3, 4, 4, Solid(4, 4, Colors.Lime), 100);

            BitmapDecoder decoder = Decode(Encode([first, second], 20, 10, 300));

            Assert.Equal(2, decoder.Frames.Count);
            Assert.Equal((20, 10), (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight));
            // The decoder gives each frame at its own size; the rest of the picture is the earlier frame left in place.
            Assert.Equal((4, 4), (decoder.Frames[1].PixelWidth, decoder.Frames[1].PixelHeight));
            AssertClose(Colors.Lime, PixelAt(decoder.Frames[1], 1, 1));
            AssertClose(Colors.Red, PixelAt(decoder.Frames[0], 6, 4));
        });
    }

    [Fact]
    public void Pixels_that_did_not_change_are_left_transparent_so_the_earlier_frame_shows_through()
    {
        Sta.Run(() =>
        {
            byte[] changed = Solid(8, 2, Colors.Red);
            for (int x = 4; x < 8; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    changed.AsSpan((y * 8 + x) * 4, 4).Clear();
                    changed[(y * 8 + x) * 4 + 1] = 255; // green
                    changed[(y * 8 + x) * 4 + 3] = 255;
                }
            }
            var first = new RecordedFrame(0, 0, 8, 2, Solid(8, 2, Colors.Red), 0);
            var second = new RecordedFrame(0, 0, 8, 2, changed, 100); // the left half is red again, as before

            BitmapDecoder decoder = Decode(Encode([first, second], 8, 2, 200));

            Assert.Equal(0, PixelAt(decoder.Frames[1], 1, 0).A);    // unchanged: see-through
            AssertClose(Colors.Lime, PixelAt(decoder.Frames[1], 6, 0)); // changed: painted
            Assert.Equal(255, PixelAt(decoder.Frames[1], 6, 0).A);
        });
    }

    [Fact]
    public void A_frame_with_many_colors_still_decodes_and_keeps_the_dominant_ones()
    {
        Sta.Run(() =>
        {
            // A gradient has far more than 256 colors; the table has to be reduced and the picture stay recognizable.
            const int width = 64;
            const int height = 64;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    pixels[i] = (byte)(x * 4);
                    pixels[i + 1] = (byte)(y * 4);
                    pixels[i + 2] = (byte)((x + y) * 2);
                    pixels[i + 3] = 255;
                }
            }

            BitmapDecoder decoder = Decode(Encode([new RecordedFrame(0, 0, width, height, pixels, 0)], width, height, 100));

            AssertClose(Color.FromRgb(0, 0, 0), PixelAt(decoder.Frames[0], 0, 0), tolerance: 16);
            AssertClose(Color.FromRgb(252, 252, 252), PixelAt(decoder.Frames[0], 63, 63), tolerance: 16);
        });
    }

    [Fact]
    public void A_big_frame_goes_through_the_compressor_table_reset_and_still_decodes()
    {
        Sta.Run(() =>
        {
            // Noise doesn't compress, so the 4096-code table fills up and has to be cleared several times.
            const int width = 200;
            const int height = 200;
            var random = new Random(7);
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte shade = (byte)(random.Next(16) * 16);
                pixels[i] = shade;
                pixels[i + 1] = (byte)(255 - shade);
                pixels[i + 2] = shade;
                pixels[i + 3] = 255;
            }

            BitmapDecoder decoder = Decode(Encode([new RecordedFrame(0, 0, width, height, pixels, 0)], width, height, 100));

            BitmapSource image = decoder.Frames[0];
            Assert.Equal((width, height), (image.PixelWidth, image.PixelHeight));
            for (int probe = 0; probe < 50; probe++)
            {
                int x = random.Next(width);
                int y = random.Next(height);
                int i = (y * width + x) * 4;
                AssertClose(Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]), PixelAt(image, x, y), tolerance: 3);
            }
        });
    }

    [Fact]
    public void The_file_starts_as_a_looping_gif_and_ends_with_the_trailer()
    {
        byte[] gif = Encode([new RecordedFrame(0, 0, 4, 4, Solid(4, 4, Colors.Blue), 0)], 4, 4, 100);

        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.ASCII.GetString(gif));
        Assert.Equal(0x3B, gif[^1]);
    }

    [Fact]
    public void Frame_delays_add_up_to_the_recording_length_without_drifting()
    {
        // Ten frames 83 ms apart: rounding each to centiseconds on its own would add up to 80 or 90, not 83 x 10 = 830 ms.
        var frames = Enumerable.Range(0, 10)
            .Select(i => new RecordedFrame(0, 0, 4, 4, Solid(4, 4, Color.FromRgb((byte)(i * 20), 0, 0)), i * 83))
            .ToList();

        byte[] gif = Encode(frames, 4, 4, 830);

        // The delay is the little-endian 16-bit value after each graphic control extension (21 F9 04 flags).
        int total = 0;
        for (int i = 0; i < gif.Length - 6; i++)
        {
            if (gif[i] == 0x21 && gif[i + 1] == 0xF9 && gif[i + 2] == 0x04)
            {
                total += gif[i + 4] | (gif[i + 5] << 8);
            }
        }
        Assert.InRange(total, 82, 84);
    }
}
