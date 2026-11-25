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
    private const double ReachUnits = 28;

    private readonly CursorMotion _motion;
    private readonly CursorStyle _style;
    private readonly double _pointerScale;
    private readonly IReadOnlyList<VideoSegment> _segments;
    private readonly CropArea? _crop;

    private CursorLayer(CursorMotion motion, CursorStyle style, double pointerScale, IReadOnlyList<VideoSegment> segments, CropArea? crop)
    {
        _motion = motion;
        _style = style;
        _pointerScale = pointerScale;
        _segments = segments;
        _crop = crop is { IsWhole: false } ? crop : null;
    }

    /// <summary>
    /// The pointer to draw on a video; null when there is none to draw: no track, a recording that has the pointer in its
    /// pictures already, or the pointer switched off.
    /// </summary>
    /// <param name="segments">The pieces of the original the video is made of, in order: the track is on the original's clock.</param>
    /// <param name="crop">The part of the recording the video keeps: the pointer and the clicks outside it are not drawn.</param>
    public static CursorLayer? For(CursorTrack? track, CursorStyle? style, IReadOnlyList<VideoSegment> segments, CropArea? crop = null) =>
        track is { PointerInPicture: false } && style is { Visible: true } && segments.Count > 0
            ? new CursorLayer(new CursorMotion(track, style), style, track.PointerScale > 0 ? track.PointerScale : 1, segments, crop)
            : null;

    /// <summary>
    /// A point of the recording (shares of it) as shares of the picture the video shows (the crop); null when it was cut away,
    /// so it is not drawn.
    /// </summary>
    public static (double X, double Y)? Shown(double x, double y, CropArea? crop)
    {
        if (crop is not { IsWhole: false } area)
        {
            return (x, y);
        }
        (double cx, double cy) = area.ToCrop(x, y);
        return CropArea.Shows(cx, cy) ? (cx, cy) : null;
    }

    /// <summary>The moment of the original shown at <paramref name="seconds"/> of the edited video.</summary>
    public static double ToOriginal(IReadOnlyList<VideoSegment> segments, double seconds)
    {
        double start = 0;
        foreach (VideoSegment segment in segments)
        {
            if (seconds < start + segment.PlayLength)
            {
                return segment.ToOriginal(seconds - start);
            }
            start += segment.PlayLength;
        }
        return segments.Count > 0 ? segments[^1].End : seconds;
    }

    /// <summary>
    /// How many pixels of the picture one unit of <see cref="CursorArt"/> takes: a pixel of the pointer on screen, grown by
    /// the size chosen and the zoom, shrunk with the recording inside a frame.
    /// </summary>
    public static double Unit(CursorStyle style, double pointerScale, ZoomView view, FrameLayout? layout) =>
        style.Size * pointerScale * view.Scale * (layout?.ContentScale ?? 1);

    /// <summary>Where a point of the recording (shares of it, or of its crop) shows in a picture of <paramref name="width"/> by <paramref name="height"/>.</summary>
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
    /// <param name="shutter">What the camera showed as the pointer's shutter opened and closed (see
    /// <see cref="MotionBlur.PointerShutterSeconds"/>), to smear a pointer that moves fast on the screen; null draws it sharp.</param>
    public void Draw(byte[] picture, int width, int height, double seconds, ZoomView view, FrameLayout? layout,
        (ZoomView Open, ZoomView Close)? shutter = null)
    {
        double original = ToOriginal(_segments, seconds);
        CursorPose? pose = _motion.PoseAt(original);
        IReadOnlyList<ClickMark> clicks = _motion.ClicksAt(original);
        if (pose == null && clicks.Count == 0)
        {
            return;
        }

        double unit = Unit(_style, _pointerScale, view, layout);
        PointF? At(double x, double y, ZoomView seen)
        {
            if (Shown(x, y, _crop) is not { } shown)
            {
                return null;
            }
            (double px, double py) = ToPicture(shown.X, shown.Y, seen, layout, width, height);
            return new PointF((float)px, (float)py);
        }

        // Where the tip was on the screen as the shutter opened or closed: the pointer's own move and the camera's together.
        PointF? Then(double half, ZoomView seen) =>
            _motion.PoseAt(ToOriginal(_segments, seconds + half)) is { } was ? At(was.X, was.Y, seen) : null;

        PointF? tip = pose is { } shownPose ? At(shownPose.X, shownPose.Y, view) : null;
        PointF? open = null, close = null;
        int smear = 1;
        if (shutter is { } camera && tip != null)
        {
            open = Then(-MotionBlur.PointerShutterSeconds / 2, camera.Open);
            close = Then(MotionBlur.PointerShutterSeconds / 2, camera.Close);
            if (open is { } a && close is { } b)
            {
                smear = MotionBlur.Samples(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));
            }
        }

        GCHandle pin = GCHandle.Alloc(picture, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject());
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (tip is { } under)
            {
                DrawShapes(graphics, under, CursorArt.PointerShapes(_style, pose!.Value), unit);
            }
            foreach (ClickMark click in clicks)
            {
                if (At(click.X, click.Y, view) is { } at)
                {
                    DrawShapes(graphics, at, CursorArt.ClickShapes(_style.Click, click), unit);
                }
            }
            if (tip is { } point && smear == 1)
            {
                DrawPointer(graphics, point, pose!.Value, unit);
            }
        }
        finally
        {
            pin.Free();
        }

        if (smear > 1) // over the rest, once GDI+ has let go of the picture
        {
            DrawSmearedPointer(picture, width, height, tip!.Value, open!.Value, close!.Value, pose!.Value, unit, smear);
        }
    }

    // The pointer drawn once on its own, then laid along its move.
    private void DrawSmearedPointer(byte[] picture, int width, int height, PointF tip, PointF open, PointF close, CursorPose pose, double unit, int samples)
    {
        int reach = (int)Math.Ceiling(ReachUnits * unit) + 2; // from the tip to the furthest the drawing goes, shadow included
        int size = reach * 2;
        int left = (int)Math.Floor(tip.X) - reach, top = (int)Math.Floor(tip.Y) - reach;
        var sprite = new byte[size * size * 4];
        GCHandle pin = GCHandle.Alloc(sprite, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(size, size, size * 4, PixelFormat.Format32bppPArgb, pin.AddrOfPinnedObject());
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawPointer(graphics, new PointF(tip.X - left, tip.Y - top), pose, unit);
        }
        finally
        {
            pin.Free();
        }
        MotionBlur.Sprite(picture, width, height, sprite, size, left, top, (open.X - tip.X, open.Y - tip.Y), (close.X - tip.X, close.Y - tip.Y), samples);
    }

    private void DrawShapes(Graphics graphics, PointF at, IEnumerable<ClickShape> shapes, double unit)
    {
        MediaColor color = CursorArt.TintColor(_style.Tint);
        foreach (ClickShape shape in shapes)
        {
            float radius = (float)(shape.Radius * unit);
            Color paint = ToDrawing(color, shape.Opacity * 255);
            if (shape.Width == 0)
            {
                using var fill = new SolidBrush(paint);
                graphics.FillEllipse(fill, at.X - radius, at.Y - radius, radius * 2, radius * 2);
            }
            else
            {
                using var pen = new Pen(paint, (float)(shape.Width * unit));
                graphics.DrawEllipse(pen, at.X - radius, at.Y - radius, radius * 2, radius * 2);
            }
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
            path.AddPolygon(CursorArt.Outline(pose.Kind).Select(point => new PointF((float)point.X, (float)point.Y)).ToArray());
        }

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(tip.X, tip.Y);
        graphics.RotateTransform((float)pose.Angle);
        graphics.ScaleTransform(scale, scale);

        // A soft shadow: the shape a little lower, twice, the second wider and fainter.
        (MediaColor fill, MediaColor edge) = CursorArt.Paint(_style.Shape);
        using (var shadow = new SolidBrush(Color.FromArgb((int)(CursorArt.ShadowAlpha / 2 * pose.Opacity), 0, 0, 0)))
        using (var blur = new Pen(Color.FromArgb((int)(CursorArt.ShadowAlpha / 3 * pose.Opacity), 0, 0, 0), (float)(CursorArt.EdgeWidth * 2.5)) { LineJoin = LineJoin.Round })
        {
            GraphicsState flat = graphics.Save();
            graphics.TranslateTransform(0, (float)CursorArt.ShadowDrop);
            graphics.DrawPath(blur, path);
            graphics.FillPath(shadow, path);
            graphics.Restore(flat);
        }

        using (var edgePen = new Pen(ToDrawing(edge, edge.A * pose.Opacity), (float)(CursorArt.EdgeWidth * 2)) { LineJoin = LineJoin.Round })
        using (var fillBrush = new SolidBrush(ToDrawing(fill, fill.A * pose.Opacity)))
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
