using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Zarpazo.Recording;

internal static class GifEncoder
{
    private const int MinDelayCentiseconds = 2; // browsers treat anything shorter as a slow default
    private const int DisposalKeep = 1 << 2;    // leave the frame in place, so the next one only paints what changed
    private const int TransparentFlag = 1;

    /// <param name="width">Size of the whole picture, which is also the size of the first frame.</param>
    /// <param name="durationMs">Total length; the last frame stays up until then.</param>
    public static void Encode(IReadOnlyList<RecordedFrame> frames, int width, int height, int durationMs, Stream output)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("There is nothing to encode.", nameof(frames));
        }

        WriteHeader(output, width, height);

        // What the screen showed after the frames written so far, to leave unchanged pixels transparent.
        var canvas = new byte[width * height * 4];
        int shownCentiseconds = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            RecordedFrame frame = frames[i];
            int endMs = i + 1 < frames.Count ? frames[i + 1].TimeMs : Math.Max(durationMs, frame.TimeMs);
            // Rounding every delay on its own would drift, so each one is the rounded end time minus what is already shown.
            int targetCentiseconds = (int)Math.Round(endMs / 10.0);
            int delay = Math.Max(MinDelayCentiseconds, targetCentiseconds - shownCentiseconds);
            shownCentiseconds += delay;

            WriteFrame(output, frame, canvas, width, first: i == 0, delay);
        }

        output.WriteByte(0x3B); // trailer
    }

    private static void WriteHeader(Stream output, int width, int height)
    {
        output.Write(Encoding.ASCII.GetBytes("GIF89a"));
        WriteShort(output, width);
        WriteShort(output, height);
        output.WriteByte(0); // no global color table: every frame carries its own
        output.WriteByte(0); // background color
        output.WriteByte(0); // pixel aspect ratio

        // Loop forever.
        output.Write([0x21, 0xFF, 0x0B]);
        output.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        output.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    private static void WriteFrame(Stream output, RecordedFrame frame, byte[] canvas, int canvasWidth, bool first, int delay)
    {
        int pixelCount = frame.Width * frame.Height;
        var unchanged = new bool[pixelCount];
        bool anyUnchanged = false;
        if (!first)
        {
            ReadOnlySpan<int> now = MemoryMarshal.Cast<byte, int>(frame.Pixels);
            ReadOnlySpan<int> before = MemoryMarshal.Cast<byte, int>(canvas);
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    if (now[y * frame.Width + x] == before[(frame.Y + y) * canvasWidth + frame.X + x])
                    {
                        unchanged[y * frame.Width + x] = true;
                        anyUnchanged = true;
                    }
                }
            }
        }

        GifPalette palette = GifPalette.Build(frame.Pixels, anyUnchanged ? unchanged : null, anyUnchanged ? 255 : 256);
        int entries = palette.Count + (anyUnchanged ? 1 : 0);
        int tableBits = Math.Max(1, (int)Math.Ceiling(Math.Log2(entries)));
        int transparentIndex = palette.Count;

        var indices = new byte[pixelCount];
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            indices[pixel] = unchanged[pixel]
                ? (byte)transparentIndex
                : (byte)palette.IndexOf(frame.Pixels[pixel * 4 + 2], frame.Pixels[pixel * 4 + 1], frame.Pixels[pixel * 4]);
        }

        // Graphic control extension: how long it stays, whether a color is see-through, and that it is left in place.
        output.Write([0x21, 0xF9, 0x04, (byte)(DisposalKeep | (anyUnchanged ? TransparentFlag : 0))]);
        WriteShort(output, delay);
        output.WriteByte((byte)(anyUnchanged ? transparentIndex : 0));
        output.WriteByte(0);

        // Image descriptor with its own color table.
        output.WriteByte(0x2C);
        WriteShort(output, frame.X);
        WriteShort(output, frame.Y);
        WriteShort(output, frame.Width);
        WriteShort(output, frame.Height);
        output.WriteByte((byte)(0x80 | (tableBits - 1)));

        var table = new byte[(1 << tableBits) * 3];
        palette.Rgb.CopyTo(table, 0);
        output.Write(table);

        GifLzw.Write(output, indices, Math.Max(2, tableBits));

        // The next frame is compared with what the screen showed, not with the reduced colors.
        for (int y = 0; y < frame.Height; y++)
        {
            Buffer.BlockCopy(frame.Pixels, y * frame.Width * 4, canvas, ((frame.Y + y) * canvasWidth + frame.X) * 4, frame.Width * 4);
        }
    }

    private static void WriteShort(Stream output, int value)
    {
        output.WriteByte((byte)(value & 0xFF));
        output.WriteByte((byte)(value >> 8));
    }
}
