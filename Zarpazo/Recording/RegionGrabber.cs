using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Takes pictures of one part of the screen, with the mouse pointer, at a fixed output size.</summary>
internal sealed class RegionGrabber : IDisposable
{
    private readonly Rectangle _region;
    private readonly Bitmap _full;
    private readonly byte[]? _shrunk; // the whole area, to shrink from; only when it is wider than the video's limit
    private readonly RecordingEffects? _effects;
    private readonly DesktopDuplicator? _duplicator; // the fast copy through the graphics card, when the area is on one screen
    private readonly bool _drawPointer;

    /// <param name="region">The part of the screen, in physical pixels.</param>
    /// <param name="effects">What to draw over the pictures (a halo around the pointer, the shortcuts pressed); none for plain pictures.</param>
    /// <param name="drawPointer">Paint the mouse pointer in; a video leaves it out, as the editor draws its own.</param>
    public RegionGrabber(Rectangle region, int width, int height, RecordingEffects? effects = null, bool drawPointer = true)
    {
        _region = region;
        _effects = effects;
        _drawPointer = drawPointer;
        Width = width;
        Height = height;
        _full = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        if (width < region.Width - 1 || height < region.Height - 1)
        {
            _shrunk = new byte[region.Width * region.Height * 4];
        }
        _duplicator = DesktopDuplicator.TryCreate(region);
    }

    /// <summary>Whether the screen is copied through the graphics card (fast) rather than with CopyFromScreen.</summary>
    public bool UsesGraphicsCard => _duplicator != null;

    /// <summary>How the graphics card copy went, for the diagnosis file; null when it is not used.</summary>
    public string? GraphicsCardReport => _duplicator?.Report;

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
    /// <param name="timeMs">The time in the recording, for effects that move (the ring that fades after a click).</param>
    public void Grab(byte[] into, long timeMs = 0)
    {
        long start = Stopwatch.GetTimestamp();
        _effects?.Update(timeMs);
        long read = Stopwatch.GetTimestamp();
        bool copied = _duplicator?.TryCopy(_full) == true;
        long copy;
        long draw;
        using (Graphics graphics = Graphics.FromImage(_full))
        {
            if (!copied)
            {
                graphics.CopyFromScreen(_region.Location, Point.Empty, _region.Size); // two screens, or the card said no this time
            }
            copy = Stopwatch.GetTimestamp();
            _effects?.DrawUnderPointer(graphics, _region.Location, timeMs);
            if (_drawPointer)
            {
                CursorPainter.Draw(graphics, _region.Location, _effects?.CursorScale ?? 1);
            }
            _effects?.DrawKeys(graphics, _region.Size);
            draw = Stopwatch.GetTimestamp();
        }

        // The video can be a pixel narrower or shorter than the area (its sizes must be even): then the extra pixel is left
        // out rather than the whole picture resampled, which at full HD took longer than a frame lasts. Only an area wider
        // than the video's limit is really shrunk.
        long scale = Stopwatch.GetTimestamp();
        BitmapData data = _full.LockBits(new Rectangle(0, 0, _region.Width, _region.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (_shrunk == null)
            {
                int rowBytes = Width * 4;
                if (Width == _region.Width)
                {
                    Marshal.Copy(data.Scan0, into, 0, rowBytes * Height); // the same width: the rows follow each other
                }
                else
                {
                    for (int row = 0; row < Height; row++)
                    {
                        Marshal.Copy(data.Scan0 + row * data.Stride, into, row * rowBytes, rowBytes);
                    }
                }
            }
            else
            {
                Marshal.Copy(data.Scan0, _shrunk, 0, _shrunk.Length);
            }
        }
        finally
        {
            _full.UnlockBits(data);
        }
        long shrink = Stopwatch.GetTimestamp();
        if (_shrunk != null)
        {
            PictureShrinker.Shrink(_shrunk, _region.Width, _region.Height, into, Width, Height);
        }
        long end = Stopwatch.GetTimestamp();
        LastTimings = new GrabTimings(Ms(start, read), Ms(read, copy), Ms(copy, draw), Ms(shrink, end), Ms(draw, shrink), copied);
    }

    /// <summary>How long each step of the last <see cref="Grab"/> took, for the diagnosis file.</summary>
    public GrabTimings LastTimings { get; private set; }

    private static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    public void Dispose()
    {
        _full.Dispose();
        _duplicator?.Dispose();
    }
}
