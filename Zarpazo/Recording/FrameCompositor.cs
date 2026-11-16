using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Windows;

namespace Zarpazo.Recording;

/// <summary>
/// Makes one picture of a framed video: the scene (the backdrop with the recording on it, rounded) seen through the zoom,
/// in a single pass. Each pixel of the result is looked up in the scene: inside the recording it is taken from the recorded
/// picture, outside from the backdrop, and along the rounded edge the two are blended so the edge stays smooth at any zoom.
/// </summary>
internal sealed class FrameCompositor
{
    private readonly FrameLayout _layout;
    private readonly byte[] _backdrop;
    private readonly byte[]? _bar;
    private readonly (byte B, byte G, byte R)? _border;
    private readonly int _sourceWidth;
    private readonly Int32Rect _area;

    /// <param name="backdrop">From <see cref="FrameBackdrop.Render"/>, the scene's size.</param>
    /// <param name="sourceWidth">The size of the recorded pictures.</param>
    /// <param name="area">The rectangle of them that is kept (the crop), which fills the recording's place; null for all of it.</param>
    /// <param name="bar">From <see cref="FrameBackdrop.RenderBar"/>: the title bar above the recording; null without one.</param>
    /// <param name="borderColor">The colour of the line along the card's edge (as wide as the layout says); null for none.</param>
    public FrameCompositor(FrameLayout layout, byte[] backdrop, int sourceWidth, int sourceHeight, Int32Rect? area = null,
        byte[]? bar = null, System.Windows.Media.Color? borderColor = null)
    {
        _layout = layout;
        _backdrop = backdrop;
        _bar = layout.BarHeight > 0 ? bar : null;
        _border = borderColor is { } color && layout.BorderPixels > 0 ? (color.B, color.G, color.R) : null;
        _sourceWidth = sourceWidth;
        _area = area ?? new Int32Rect(0, 0, sourceWidth, sourceHeight);
    }

    public int Width => _layout.Width;

    public int Height => _layout.Height;

    /// <param name="source">A recorded picture, BGRA.</param>
    /// <param name="target">The scene's size; it is filled whole.</param>
    /// <param name="view">What the camera shows, in shares of the scene.</param>
    public void Render(byte[] source, byte[] target, ZoomView view)
    {
        int width = _layout.Width;
        int height = _layout.Height;
        Int32Rect card = _layout.Card; // the recording and the bar above it: the rounded rectangle
        double left = card.X;
        double top = card.Y;
        double cardWidth = card.Width;
        double cardHeight = card.Height;
        double contentTop = _layout.Content.Y;
        double radius = Math.Min(_layout.RadiusPixels, Math.Min(cardWidth, cardHeight) / 2);
        double centerX = left + cardWidth / 2;
        double centerY = top + cardHeight / 2;
        double halfInnerX = cardWidth / 2 - radius;
        double halfInnerY = cardHeight / 2 - radius;
        double toSourceX = _area.Width / (double)_layout.Content.Width;
        double toSourceY = _area.Height / (double)_layout.Content.Height;
        Int32Rect area = _area;
        var whole = new Int32Rect(0, 0, width, height);
        var barRect = new Int32Rect(0, 0, card.Width, Math.Max(1, _layout.BarHeight));
        byte[]? bar = _bar;
        double line = _border is null ? 0 : _layout.BorderPixels;
        (byte B, byte G, byte R) lineColor = _border ?? default;
        double pixel = view.Width; // how much of the scene one pixel of the result covers, for the soft edge
        // With the whole scene in view each pixel of the result is the same pixel of the backdrop, so what the card does not
        // reach is copied as it is. That is most of a tall or square video, and looking each one up cost more than the rest together.
        bool fullView = view.IsFull;
        double bottom = top + cardHeight, right = left + cardWidth;
        double margin = pixel + line; // deeper inside than this, the edge and the line are out of reach

        // The recorded picture's column for each column of the result, worked out once: it does not change from row to row.
        int maxX = area.X + area.Width - 1, maxY = area.Y + area.Height - 1;
        int[] columnA = new int[width], columnB = new int[width], columnWeight = new int[width];
        for (int x = 0; x < width; x++)
        {
            double sceneX = view.X * width + (x + 0.5) * view.Width;
            double sx = Math.Clamp(area.X + (sceneX - left) * toSourceX - 0.5, area.X, maxX);
            int x0 = (int)sx;
            columnA[x] = x0 * 4;
            columnB[x] = Math.Min(x0 + 1, maxX) * 4;
            columnWeight[x] = (int)((sx - x0) * 256);
        }

        Parallel.For(0, height, y =>
        {
            double sceneY = view.Y * height + (y + 0.5) * view.Height;
            int rowStart = y * width * 4;
            if (fullView && (sceneY < top - 1 || sceneY > bottom + 1))
            {
                Buffer.BlockCopy(_backdrop, rowStart, target, rowStart, width * 4);
                return;
            }

            // The columns the card does not reach (with the whole scene in view) are copied from the backdrop in one go.
            int first = 0, end = width;
            if (fullView)
            {
                while (first < width && view.X * width + (first + 0.5) * view.Width < left - 1)
                {
                    first++;
                }
                while (end > first && view.X * width + (end - 0.5) * view.Width > right + 1)
                {
                    end--;
                }
                Buffer.BlockCopy(_backdrop, rowStart, target, rowStart, first * 4);
                Buffer.BlockCopy(_backdrop, rowStart + end * 4, target, rowStart + end * 4, (width - end) * 4);
            }

            // Rows well inside the card's straight part and below its bar: most pixels need no edge, line or corner, only the
            // recorded picture's colour, so they go through a short loop and the general one handles what is left at the ends.
            double qy = Math.Abs(sceneY - centerY) - halfInnerY;
            int from = first, to = first; // with no plain stretch, everything goes through the general loop
            bool plain = qy <= -margin && (bar == null || sceneY >= contentTop);
            if (plain)
            {
                to = end;
                while (from < end && Math.Abs(view.X * width + (from + 0.5) * view.Width - centerX) - halfInnerX > -margin)
                {
                    from++;
                }
                while (to > from && Math.Abs(view.X * width + (to - 0.5) * view.Width - centerX) - halfInnerX > -margin)
                {
                    to--;
                }
                FillPlain(source, target, y, sceneY, from, to);
            }

            General(first, from, y, sceneY, rowStart + first * 4);
            General(to, end, y, sceneY, rowStart + to * 4);
        });

        // The short loop: the recorded picture sampled as Sample does, with the columns already worked out.
        void FillPlain(byte[] picture, byte[] result, int y, double sceneY, int from, int to)
        {
            double sy = Math.Clamp(area.Y + (sceneY - contentTop) * toSourceY - 0.5, area.Y, maxY);
            int y0 = (int)sy;
            int y1 = Math.Min(y0 + 1, maxY);
            int wy = (int)((sy - y0) * 256);
            int iy = 256 - wy;
            ReadOnlySpan<byte> upperRow = picture.AsSpan(y0 * _sourceWidth * 4, _sourceWidth * 4);
            ReadOnlySpan<byte> lowerRow = picture.AsSpan(y1 * _sourceWidth * 4, _sourceWidth * 4);
            ReadOnlySpan<int> starts = columnA, ends = columnB, weights = columnWeight;
            Span<byte> row = result.AsSpan(y * width * 4, width * 4);
            Vector128<int> belowWeight = Vector128.Create(wy), aboveWeight = Vector128.Create(iy), round = Vector128.Create(1 << 15);
            for (int x = from; x < to; x++)
            {
                int a = starts[x], b = ends[x];
                Vector128<int> right = Vector128.Create(weights[x]), left = Vector128.Create(256 - weights[x]);
                // The four channels at once: blended across the row first, then between the two rows, as Sample does.
                Vector128<int> upper = Widen(upperRow, a) * left + Widen(upperRow, b) * right;
                Vector128<int> lower = Widen(lowerRow, a) * left + Widen(lowerRow, b) * right;
                Vector128<int> blended = Vector128.ShiftRightLogical(upper * aboveWeight + lower * belowWeight + round, 16);
                // Each channel is 0 to 255 now: one byte each, and the picture is opaque.
                Vector128<short> narrow = Vector128.Narrow(blended, blended);
                uint pixel = Vector128.Narrow(narrow.AsUInt16(), narrow.AsUInt16()).AsUInt32().ToScalar() | 0xFF000000u;
                MemoryMarshal.Write(row.Slice(x * 4, 4), in pixel);
            }
        }

        // One recorded pixel's four bytes as four whole numbers.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Widen(ReadOnlySpan<byte> pixels, int at) =>
            Vector128.WidenLower(Vector128.WidenLower(Vector128.CreateScalar(MemoryMarshal.Read<uint>(pixels.Slice(at, 4))).AsByte())).AsInt32();

        // Every pixel the long way: the edge, the line, the corners, the bar and what is behind the card.
        void General(int from, int to, int y, double sceneY, int o)
        {
            Span<byte> inside = stackalloc byte[4];
            Span<byte> behind = stackalloc byte[4];
            for (int x = from; x < to; x++, o += 4)
            {
                double sceneX = view.X * width + (x + 0.5) * view.Width;

                // How far the point is outside the rounded rectangle (negative inside), turned into how much of the pixel it
                // covers, and into how much of it the line along the edge covers.
                double qx = Math.Abs(sceneX - centerX) - halfInnerX;
                double qy = Math.Abs(sceneY - centerY) - halfInnerY;
                double cover, ring = 0;
                if (qx <= -margin && qy <= -margin)
                {
                    cover = 1; // well inside the straight part: no corner or line to work out
                }
                else
                {
                    double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0))
                        + Math.Min(Math.Max(qx, qy), 0) - radius;
                    cover = Math.Clamp(0.5 - outside / pixel, 0, 1);
                    if (line > 0)
                    {
                        ring = Math.Clamp(0.5 + (outside + line) / pixel, 0, 1);
                    }
                }

                if (cover <= 0)
                {
                    Sample(_backdrop, width, whole, sceneX - 0.5, sceneY - 0.5, target.AsSpan(o, 4));
                    continue;
                }

                // What the card shows here: the bar's pixel above the recording, the recorded picture's below.
                if (bar != null && sceneY < contentTop)
                {
                    Sample(bar, card.Width, barRect, sceneX - left - 0.5, sceneY - top - 0.5, inside);
                }
                else
                {
                    Sample(source, _sourceWidth, area, area.X + (sceneX - left) * toSourceX - 0.5, area.Y + (sceneY - contentTop) * toSourceY - 0.5, inside);
                }
                if (ring > 0)
                {
                    inside[0] = (byte)(inside[0] * (1 - ring) + lineColor.B * ring + 0.5);
                    inside[1] = (byte)(inside[1] * (1 - ring) + lineColor.G * ring + 0.5);
                    inside[2] = (byte)(inside[2] * (1 - ring) + lineColor.R * ring + 0.5);
                }

                if (cover >= 1)
                {
                    inside.CopyTo(target.AsSpan(o, 4));
                    continue;
                }
                Sample(_backdrop, width, whole, sceneX - 0.5, sceneY - 0.5, behind);
                for (int c = 0; c < 3; c++)
                {
                    target[o + c] = (byte)(inside[c] * cover + behind[c] * (1 - cover) + 0.5);
                }
                target[o + 3] = 255;
            }
        }
    }

    // Bilinear: the four pixels around the point, weighted by how close it is to each, never from outside the rectangle given
    // (so a crop's edge does not bleed in what was cut away).
    private static void Sample(byte[] picture, int width, Int32Rect inside, double x, double y, Span<byte> into)
    {
        int maxX = inside.X + inside.Width - 1, maxY = inside.Y + inside.Height - 1;
        x = Math.Clamp(x, inside.X, maxX);
        y = Math.Clamp(y, inside.Y, maxY);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, maxX), y1 = Math.Min(y0 + 1, maxY);
        int wx = (int)((x - x0) * 256), wy = (int)((y - y0) * 256);
        int r0 = y0 * width * 4, r1 = y1 * width * 4;
        int a = x0 * 4, b = x1 * 4;
        for (int c = 0; c < 3; c++)
        {
            int upper = picture[r0 + a + c] * (256 - wx) + picture[r0 + b + c] * wx;
            int lower = picture[r1 + a + c] * (256 - wx) + picture[r1 + b + c] * wx;
            into[c] = (byte)((upper * (256 - wy) + lower * wy + (1 << 15)) >> 16);
        }
        into[3] = 255;
    }
}
