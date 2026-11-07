using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Takes pictures of one part of the screen, with the mouse pointer, at a fixed output size.</summary>
internal sealed class RegionGrabber : IDisposable
{
    private readonly Rectangle _region;
    private readonly Bitmap _full;
    private readonly Bitmap? _scaled; // only when the output is not the size of the area

    /// <param name="region">The part of the screen, in physical pixels.</param>
    public RegionGrabber(Rectangle region, int width, int height)
    {
        _region = region;
        Width = width;
        Height = height;
        _full = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        if (width != region.Width || height != region.Height)
        {
            _scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The output size for an area, shrunk (keeping its proportions) so its longest side is at most <paramref name="maxSide"/>.</summary>
    public static (int Width, int Height) FitSize(Rectangle region, int maxSide, bool even)
    {
        double scale = Math.Min(1, (double)maxSide / Math.Max(region.Width, region.Height));
        int width = Math.Max(1, (int)Math.Round(region.Width * scale));
        int height = Math.Max(1, (int)Math.Round(region.Height * scale));
        if (even)
        {
            // H.264 stores the picture in blocks of two pixels.
            width = Math.Max(2, width & ~1);
            height = Math.Max(2, height & ~1);
        }
        return (width, height);
    }

    /// <summary>Fills <paramref name="into"/> (Width x Height x 4 bytes, BGRA) with what the area shows right now.</summary>
    public void Grab(byte[] into)
    {
        using (Graphics graphics = Graphics.FromImage(_full))
        {
            graphics.CopyFromScreen(_region.Location, Point.Empty, _region.Size);
            CursorPainter.Draw(graphics, _region.Location);
        }

        Bitmap source = _full;
        if (_scaled != null)
        {
            using Graphics graphics = Graphics.FromImage(_scaled);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(_full, 0, 0, Width, Height);
            source = _scaled;
        }

        BitmapData data = source.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Scan0, into, 0, into.Length); // 32 bits per pixel: the rows have no padding
        }
        finally
        {
            source.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        _full.Dispose();
        _scaled?.Dispose();
    }
}
