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
    private readonly int _sourceWidth;
    private readonly Int32Rect _area;

    /// <param name="backdrop">From <see cref="FrameBackdrop.Render"/>, the scene's size.</param>
    /// <param name="sourceWidth">The size of the recorded pictures.</param>
    /// <param name="area">The rectangle of them that is kept (the crop), which fills the recording's place; null for all of it.</param>
    public FrameCompositor(FrameLayout layout, byte[] backdrop, int sourceWidth, int sourceHeight, Int32Rect? area = null)
    {
        _layout = layout;
        _backdrop = backdrop;
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
        double left = _layout.Content.X;
        double top = _layout.Content.Y;
        double contentWidth = _layout.Content.Width;
        double contentHeight = _layout.Content.Height;
        double radius = Math.Min(_layout.RadiusPixels, Math.Min(contentWidth, contentHeight) / 2);
        double centerX = left + contentWidth / 2;
        double centerY = top + contentHeight / 2;
        double halfInnerX = contentWidth / 2 - radius;
        double halfInnerY = contentHeight / 2 - radius;
        double toSourceX = _area.Width / contentWidth;
        double toSourceY = _area.Height / contentHeight;
        Int32Rect area = _area;
        var whole = new Int32Rect(0, 0, width, height);
        double pixel = view.Width; // how much of the scene one pixel of the result covers, for the soft edge

        Parallel.For(0, height, y =>
        {
            double sceneY = view.Y * height + (y + 0.5) * view.Height;
            int o = y * width * 4;
            Span<byte> inside = stackalloc byte[4];
            Span<byte> behind = stackalloc byte[4];
            for (int x = 0; x < width; x++, o += 4)
            {
                double sceneX = view.X * width + (x + 0.5) * view.Width;

                // How far the point is outside the rounded rectangle (negative inside), turned into how much of the pixel it covers.
                double qx = Math.Abs(sceneX - centerX) - halfInnerX;
                double qy = Math.Abs(sceneY - centerY) - halfInnerY;
                double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0))
                    + Math.Min(Math.Max(qx, qy), 0) - radius;
                double cover = Math.Clamp(0.5 - outside / pixel, 0, 1);

                double sourceX = area.X + (sceneX - left) * toSourceX - 0.5;
                double sourceY = area.Y + (sceneY - top) * toSourceY - 0.5;
                if (cover >= 1)
                {
                    Sample(source, _sourceWidth, area, sourceX, sourceY, target.AsSpan(o, 4));
                }
                else if (cover <= 0)
                {
                    Sample(_backdrop, width, whole, sceneX - 0.5, sceneY - 0.5, target.AsSpan(o, 4));
                }
                else
                {
                    Sample(source, _sourceWidth, area, sourceX, sourceY, inside);
                    Sample(_backdrop, width, whole, sceneX - 0.5, sceneY - 0.5, behind);
                    for (int c = 0; c < 3; c++)
                    {
                        target[o + c] = (byte)(inside[c] * cover + behind[c] * (1 - cover) + 0.5);
                    }
                    target[o + 3] = 255;
                }
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
