using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;

namespace Zarpazo.Recording;

/// <summary>
/// Draws the overlays (blurs, texts, arrows) onto the pictures of a video being saved, after the zoom and the frame and before
/// the pointer. Drawn at the final size rather than stretched with the picture, so a text stays sharp however close the camera is.
/// </summary>
internal sealed class OverlayLayer
{
    private const double BlurRadius = 0.02; // of the picture's height, at the size things have with no zoom
    private const double ArrowHeadLength = 4.2; // in line widths
    private const double ArrowHeadHalfAngle = 0.5; // radians

    private readonly IReadOnlyList<VideoOverlay> _overlays;
    private readonly IReadOnlyList<VideoSegment> _segments;
    private readonly CropArea? _crop;

    private OverlayLayer(IReadOnlyList<VideoOverlay> overlays, IReadOnlyList<VideoSegment> segments, CropArea? crop)
    {
        _overlays = overlays;
        _segments = segments;
        _crop = crop is { IsWhole: false } ? crop : null;
    }

    /// <summary>The overlays to draw on a video; null when there are none.</summary>
    /// <param name="segments">The pieces of the original the video is made of, in order: the overlays are on the original's clock.</param>
    /// <param name="crop">The part of the recording the video keeps.</param>
    public static OverlayLayer? For(IReadOnlyList<VideoOverlay>? overlays, IReadOnlyList<VideoSegment> segments, CropArea? crop = null) =>
        overlays is { Count: > 0 } && segments.Count > 0 ? new OverlayLayer(overlays, segments, crop) : null;

    /// <summary>How many pixels a share of the video's height takes in a picture: grown by the zoom, shrunk with the recording inside a frame.</summary>
    public static double Pixels(double share, int height, ZoomView view, FrameLayout? layout) =>
        share * height * view.Scale * (layout is { } frame ? frame.Content.Height / (double)frame.Height : 1);

    /// <summary>Draws onto one picture of the edited video.</summary>
    /// <param name="picture">BGRA, <paramref name="width"/> by <paramref name="height"/>, the zoom and the frame already applied.</param>
    /// <param name="seconds">The picture's time in the edited video.</param>
    /// <param name="view">What the camera showed in it, in shares of the scene.</param>
    /// <param name="layout">Where the recording sits in its frame; null without one.</param>
    public void Draw(byte[] picture, int width, int height, double seconds, ZoomView view, FrameLayout? layout)
    {
        double original = CursorLayer.ToOriginal(_segments, seconds);
        List<VideoOverlay>? drawn = null;
        foreach (VideoOverlay overlay in _overlays)
        {
            if (overlay.OpacityAt(original) <= 0)
            {
                continue;
            }
            if (overlay.Kind == OverlayKind.Blur)
            {
                Blur(picture, width, height, overlay, view, layout);
            }
            else
            {
                (drawn ??= []).Add(overlay);
            }
        }
        if (drawn == null)
        {
            return;
        }

        GCHandle pin = GCHandle.Alloc(picture, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject());
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            foreach (VideoOverlay overlay in drawn)
            {
                double opacity = overlay.OpacityAt(original);
                if (overlay.Kind == OverlayKind.Arrow)
                {
                    DrawArrow(graphics, overlay, opacity, width, height, view, layout);
                }
                else
                {
                    DrawText(graphics, overlay, opacity, width, height, view, layout);
                }
            }
        }
        finally
        {
            pin.Free();
        }
    }

    // A point of the recording (shares of it) in the picture's pixels.
    private PointF At(double x, double y, int width, int height, ZoomView view, FrameLayout? layout)
    {
        (double cx, double cy) = _crop is { } area ? area.ToCrop(x, y) : (x, y);
        (double px, double py) = CursorLayer.ToPicture(cx, cy, view, layout, width, height);
        return new PointF((float)px, (float)py);
    }

    private void Blur(byte[] picture, int width, int height, VideoOverlay overlay, ZoomView view, FrameLayout? layout)
    {
        PointF from = At(overlay.X, overlay.Y, width, height, view, layout);
        PointF to = At(overlay.X2, overlay.Y2, width, height, view, layout);
        int left = Math.Clamp((int)Math.Floor(Math.Min(from.X, to.X)), 0, width);
        int top = Math.Clamp((int)Math.Floor(Math.Min(from.Y, to.Y)), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(Math.Max(from.X, to.X)), 0, width);
        int bottom = Math.Clamp((int)Math.Ceiling(Math.Max(from.Y, to.Y)), 0, height);
        int radius = (int)Math.Round(Pixels(BlurRadius, height, view, layout));
        BoxBlur.Apply(picture, width, new Int32Rect(left, top, right - left, bottom - top), Math.Max(2, radius));
    }

    private void DrawArrow(Graphics graphics, VideoOverlay overlay, double opacity, int width, int height, ZoomView view, FrameLayout? layout)
    {
        PointF tail = At(overlay.X, overlay.Y, width, height, view, layout);
        PointF head = At(overlay.X2, overlay.Y2, width, height, view, layout);
        float thickness = (float)Math.Max(1.5, Pixels(overlay.Extent, height, view, layout));
        double length = Math.Sqrt(Math.Pow(head.X - tail.X, 2) + Math.Pow(head.Y - tail.Y, 2));
        if (length < 1)
        {
            return;
        }

        double angle = Math.Atan2(head.Y - tail.Y, head.X - tail.X);
        float headLength = (float)Math.Min(thickness * ArrowHeadLength, length * 0.6);
        PointF Back(double turn) => new(
            head.X - (float)(headLength * Math.Cos(angle + turn)),
            head.Y - (float)(headLength * Math.Sin(angle + turn)));
        // The line stops inside the head, so its round end does not poke out of the point.
        var neck = new PointF(head.X - (float)(headLength * 0.7 * Math.Cos(angle)), head.Y - (float)(headLength * 0.7 * Math.Sin(angle)));
        PointF[] triangle = [head, Back(ArrowHeadHalfAngle), Back(-ArrowHeadHalfAngle)];

        System.Windows.Media.Color paint = overlay.Paint;
        for (int pass = 0; pass < 2; pass++)
        {
            bool shadow = pass == 0;
            GraphicsState state = graphics.Save();
            if (shadow)
            {
                graphics.TranslateTransform(0, thickness * 0.35f);
            }
            Color color = shadow ? Color.FromArgb((int)(110 * opacity), 0, 0, 0) : Color.FromArgb((int)Math.Round(255 * opacity), paint.R, paint.G, paint.B);
            using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Flat };
            using var brush = new SolidBrush(color);
            graphics.DrawLine(pen, tail, neck);
            graphics.FillPolygon(brush, triangle);
            graphics.Restore(state);
        }
    }

    private void DrawText(Graphics graphics, VideoOverlay overlay, double opacity, int width, int height, ZoomView view, FrameLayout? layout)
    {
        if (string.IsNullOrWhiteSpace(overlay.Text))
        {
            return;
        }

        PointF at = At(overlay.X, overlay.Y, width, height, view, layout);
        float size = (float)Math.Max(6, Pixels(overlay.Extent, height, view, layout));
        System.Windows.Media.Color paint = overlay.Paint;
        using var font = new Font("Segoe UI", size, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var shadow = new SolidBrush(Color.FromArgb((int)(150 * opacity), 0, 0, 0));
        using var ink = new SolidBrush(Color.FromArgb((int)Math.Round(255 * opacity), paint.R, paint.G, paint.B));
        float offset = Math.Max(1f, size * 0.05f);
        graphics.DrawString(overlay.Text, font, shadow, at.X + offset, at.Y + offset);
        graphics.DrawString(overlay.Text, font, ink, at);
    }
}
