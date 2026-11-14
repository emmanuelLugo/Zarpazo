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

        Parallel.For(0, height, y =>
        {
            double sceneY = view.Y * height + (y + 0.5) * view.Height;
            int o = y * width * 4;
            if (fullView && (sceneY < top - 1 || sceneY > bottom + 1))
            {
                Buffer.BlockCopy(_backdrop, o, target, o, width * 4);
                return;
            }

            Span<byte> inside = stackalloc byte[4];
            Span<byte> behind = stackalloc byte[4];
            for (int x = 0; x < width; x++, o += 4)
            {
                double sceneX = view.X * width + (x + 0.5) * view.Width;
                if (fullView && (sceneX < left - 1 || sceneX > right + 1))
                {
                    Buffer.BlockCopy(_backdrop, o, target, o, 4);
                    continue;
                }

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
        });
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
