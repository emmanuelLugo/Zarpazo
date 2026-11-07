using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

internal static class FrameDiff
{
    private const int BytesPerPixel = 4;

    /// <summary>The smallest rectangle holding every pixel that differs between two frames of the same size; null if they are identical.</summary>
    public static (int X, int Y, int Width, int Height)? ChangedRect(byte[] previous, byte[] current, int width, int height)
    {
        int stride = width * BytesPerPixel;
        int top = -1;
        int bottom = -1;
        int left = width;
        int right = -1;

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> before = previous.AsSpan(y * stride, stride);
            ReadOnlySpan<byte> after = current.AsSpan(y * stride, stride);
            if (before.SequenceEqual(after))
            {
                continue;
            }

            top = top < 0 ? y : top;
            bottom = y;

            // Whole pixels at a time: one comparison instead of four.
            ReadOnlySpan<int> a = MemoryMarshal.Cast<byte, int>(before);
            ReadOnlySpan<int> b = MemoryMarshal.Cast<byte, int>(after);
            int first = 0;
            while (a[first] == b[first])
            {
                first++;
            }
            int last = width - 1;
            while (a[last] == b[last])
            {
                last--;
            }
            left = Math.Min(left, first);
            right = Math.Max(right, last);
        }

        return top < 0 ? null : (left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>Copies a rectangle out of a frame into its own tightly packed array.</summary>
    public static byte[] Crop(byte[] frame, int frameWidth, int x, int y, int width, int height)
    {
        var result = new byte[width * height * BytesPerPixel];
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(frame, ((y + row) * frameWidth + x) * BytesPerPixel, result, row * width * BytesPerPixel, width * BytesPerPixel);
        }
        return result;
    }
}
