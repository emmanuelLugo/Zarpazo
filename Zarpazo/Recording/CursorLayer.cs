using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MediaColor = System.Windows.Media.Color;

namespace Zarpazo.Recording;

/// <summary>
/// Draws the pointer and the clicks onto the pictures of a video being saved, after the zoom and the frame: drawn at the
/// final size rather than stretched with the picture, the pointer stays sharp however close the camera is.
/// </summary>
internal sealed class CursorLayer
{
    private readonly CursorMotion _motion;
    private readonly CursorStyle _style;
    private readonly double _pointerScale;
    private readonly IReadOnlyList<VideoSegment> _segments;

    private CursorLayer(CursorMotion motion, CursorStyle style, double pointerScale, IReadOnlyList<VideoSegment> segments)
    {
        _motion = motion;
        _style = style;
        _pointerScale = pointerScale;
        _segments = segments;
    }

    /// <summary>
    /// The pointer to draw on a video; null when there is none to draw: no track, a recording that has the pointer in its
    /// pictures already, or the pointer switched off.
    /// </summary>
    /// <param name="segments">The pieces of the original the video is made of, in order: the track is on the original's clock.</param>
    public static CursorLayer? For(CursorTrack? track, CursorStyle? style, IReadOnlyList<VideoSegment> segments) =>
        track is { PointerInPicture: false } && style is { Visible: true } && segments.Count > 0
            ? new CursorLayer(new CursorMotion(track, style), style, track.PointerScale > 0 ? track.PointerScale : 1, segments)
            : null;

    /// <summary>The moment of the original shown at <paramref name="seconds"/> of the edited video.</summary>
    public static double ToOriginal(IReadOnlyList<VideoSegment> segments, double seconds)
    {
        double start = 0;
        foreach (VideoSegment segment in segments)
        {
            if (seconds < start + segment.Length)
            {
                return segment.Start + Math.Max(0, seconds - start);
            }
            start += segment.Length;
        }
        return segments.Count > 0 ? segments[^1].End : seconds;
    }

    /// <summary>
    /// How many pixels of the picture one unit of <see cref="CursorArt"/> takes: a pixel of the pointer on screen, grown by
    /// the size chosen and the zoom, shrunk with the recording inside a frame.
    /// </summary>
    public static double Unit(CursorStyle style, double pointerScale, ZoomView view, FrameLayout? layout) =>
        style.Size * pointerScale * view.Scale * (layout is { } frame ? frame.Content.Width / (double)frame.Width : 1);

    /// <summary>Where a point of the recording (shares of it) shows in a picture of <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static (double X, double Y) ToPicture(double x, double y, ZoomView view, FrameLayout? layout, double width, double height)
    {
        (double sx, double sy) = layout is { } frame ? frame.ToScene(x, y) : (x, y);
        return ((sx - view.X) / view.Width * width, (sy - view.Y) / view.Height * height);
    }

    /// <summary>Draws onto one picture of the edited video.</summary>
    /// <param name="picture">BGRA, <paramref name="width"/> by <paramref name="height"/>, the zoom and the frame already applied.</param>
    /// <param name="seconds">The picture's time in the edited video.</param>
    /// <param name="view">What the camera showed in it, in shares of the scene (of the recording, without a frame).</param>
    /// <param name="layout">Where the recording sits in its frame; null without one.</param>
    public void Draw(byte[] picture, int width, int height, double seconds, ZoomView view, FrameLayout? layout)
    {
        double original = ToOriginal(_segments, seconds);
        CursorPose? pose = _motion.PoseAt(original);
        IReadOnlyList<ClickMark> clicks = _motion.ClicksAt(original);
        if (pose == null && clicks.Count == 0)
        {
            return;
        }

        double unit = Unit(_style, _pointerScale, view, layout);
        PointF At(double x, double y)
        {
            (double px, double py) = ToPicture(x, y, view, layout, width, height);
            return new PointF((float)px, (float)py);
        }

        GCHandle pin = GCHandle.Alloc(picture, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject());
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            foreach (ClickMark click in clicks)
            {
                DrawClick(graphics, At(click.X, click.Y), click.Progress, unit);
            }
            if (pose is { } p)
            {
                DrawPointer(graphics, At(p.X, p.Y), p, unit);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    private void DrawClick(Graphics graphics, PointF at, double progress, double unit)
    {
        double growth = CursorArt.ClickGrowth(progress);
        int alpha = (int)Math.Round(255 * CursorArt.ClickOpacity(progress));
        if (alpha <= 0)
        {
            return;
        }

        if (_style.Click == ClickEffect.Pulse)
        {
            float radius = (float)((CursorArt.PulseStartRadius + (CursorArt.PulseEndRadius - CursorArt.PulseStartRadius) * growth) * unit);
            using var fill = new SolidBrush(ToDrawing(CursorArt.ClickColor, alpha * 0.45));
            graphics.FillEllipse(fill, at.X - radius, at.Y - radius, radius * 2, radius * 2);
        }
        else
        {
            float radius = (float)((CursorArt.RippleStartRadius + (CursorArt.RippleEndRadius - CursorArt.RippleStartRadius) * growth) * unit);
            using var pen = new Pen(ToDrawing(CursorArt.ClickColor, alpha), (float)(CursorArt.RippleWidth * unit));
            graphics.DrawEllipse(pen, at.X - radius, at.Y - radius, radius * 2, radius * 2);
        }
    }

    private void DrawPointer(Graphics graphics, PointF tip, CursorPose pose, double unit)
    {
        float scale = (float)(unit * pose.Scale);
        using var path = new GraphicsPath();
        if (_style.Shape == CursorShape.Dot)
        {
            path.AddEllipse((float)-CursorArt.DotRadius, (float)-CursorArt.DotRadius, (float)CursorArt.DotRadius * 2, (float)CursorArt.DotRadius * 2);
        }
        else
        {
            path.AddPolygon(CursorArt.Arrow.Select(point => new PointF((float)point.X, (float)point.Y)).ToArray());
        }

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(tip.X, tip.Y);
        graphics.RotateTransform((float)pose.Angle);
        graphics.ScaleTransform(scale, scale);

        // A soft shadow: the shape a little lower, twice, the second wider and fainter.
        (MediaColor fill, MediaColor edge) = CursorArt.Paint(_style.Shape);
        using (var shadow = new SolidBrush(Color.FromArgb(CursorArt.ShadowAlpha / 2, 0, 0, 0)))
        using (var blur = new Pen(Color.FromArgb(CursorArt.ShadowAlpha / 3, 0, 0, 0), (float)(CursorArt.EdgeWidth * 2.5)) { LineJoin = LineJoin.Round })
        {
            GraphicsState flat = graphics.Save();
            graphics.TranslateTransform(0, (float)CursorArt.ShadowDrop);
            graphics.DrawPath(blur, path);
            graphics.FillPath(shadow, path);
            graphics.Restore(flat);
        }

        using (var edgePen = new Pen(ToDrawing(edge, edge.A), (float)(CursorArt.EdgeWidth * 2)) { LineJoin = LineJoin.Round })
        using (var fillBrush = new SolidBrush(ToDrawing(fill, fill.A)))
        {
            // The edge is drawn twice as wide and the fill over it, so the edge shows only outside the shape.
            graphics.DrawPath(edgePen, path);
            graphics.FillPath(fillBrush, path);
        }
        graphics.Restore(state);
    }

    private static Color ToDrawing(MediaColor color, double alpha) =>
        Color.FromArgb((int)Math.Clamp(Math.Round(alpha), 0, 255), color.R, color.G, color.B);
}
