using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>
/// The drawings of the pointer and of the clicks, in units of one pixel of a pointer on a screen at 100 %, with the tip (the
/// point that clicks) at (0, 0). The saved video and the editor's preview both draw from here, so they look the same.
/// </summary>
internal static class CursorArt
{
    /// <summary>The arrow's outline, from the tip down the left side, round the tail and back.</summary>
    public static readonly Point[] Arrow =
    [
        new(0, 0), new(0, 17.2), new(4.3, 13.3), new(6.9, 19.3), new(9.7, 18.1), new(7.1, 12.2), new(12.4, 12.2),
    ];

    // The other pointers Windows changes to, each with the point that clicks at (0, 0): the finger's tip, or the middle.
    private static readonly Point[] Hand =
    [
        new(-1.5, 0.7), new(-0.6, 0), new(0.6, 0), new(1.5, 0.7), new(1.8, 7.8), new(2.6, 6.9), new(3.9, 6.9), new(4.6, 8.2),
        new(5.4, 7.5), new(6.7, 7.6), new(7.4, 9), new(8.2, 8.5), new(9.4, 8.8), new(10, 10.2), new(10, 13.6), new(8.8, 17),
        new(8.4, 19.4), new(1, 19.4), new(-1.4, 16.2), new(-4.8, 11.9), new(-4.5, 10.5), new(-3.1, 10.2), new(-1.7, 11.5),
    ];

    private static readonly Point[] TextBeam =
    [
        new(-4, -9), new(-1.2, -9), new(0, -8.2), new(1.2, -9), new(4, -9), new(4, -7.4), new(0.9, -7.4), new(0.9, 7.4),
        new(4, 7.4), new(4, 9), new(1.2, 9), new(0, 8.2), new(-1.2, 9), new(-4, 9), new(-4, 7.4), new(-0.9, 7.4),
        new(-0.9, -7.4), new(-4, -7.4),
    ];

    private static readonly Point[] DoubleArrow =
    [
        new(-9.5, 0), new(-4.5, -4.8), new(-4.5, -1.4), new(4.5, -1.4), new(4.5, -4.8), new(9.5, 0), new(4.5, 4.8),
        new(4.5, 1.4), new(-4.5, 1.4), new(-4.5, 4.8),
    ];

    private static readonly Point[] FourArrows =
    [
        new(0, -10), new(4, -6), new(1.3, -6), new(1.3, -1.3), new(6, -1.3), new(6, -4), new(10, 0), new(6, 4), new(6, 1.3),
        new(1.3, 1.3), new(1.3, 6), new(4, 6), new(0, 10), new(-4, 6), new(-1.3, 6), new(-1.3, 1.3), new(-6, 1.3), new(-6, 4),
        new(-10, 0), new(-6, -4), new(-6, -1.3), new(-1.3, -1.3), new(-1.3, -6), new(-4, -6),
    ];

    private static readonly Point[] VerticalArrow = Turned(DoubleArrow, 90);
    private static readonly Point[] DownArrow = Turned(DoubleArrow, 45);
    private static readonly Point[] UpArrow = Turned(DoubleArrow, -45);

    /// <summary>The outline of one of Windows' pointers, as an arrow style draws it.</summary>
    public static Point[] Outline(CursorKind kind) => kind switch
    {
        CursorKind.Hand => Hand,
        CursorKind.Text => TextBeam,
        CursorKind.ResizeHorizontal => DoubleArrow,
        CursorKind.ResizeVertical => VerticalArrow,
        CursorKind.ResizeDiagonalDown => DownArrow,
        CursorKind.ResizeDiagonalUp => UpArrow,
        CursorKind.Move => FourArrows,
        _ => Arrow,
    };

    private static Point[] Turned(Point[] points, double degrees)
    {
        var turn = System.Windows.Media.Matrix.Identity;
        turn.Rotate(degrees);
        return points.Select(turn.Transform).ToArray();
    }

    /// <summary>How wide the contrasting edge round the arrow and the dot is.</summary>
    public const double EdgeWidth = 1.6;

    public const double DotRadius = 6.5;

    /// <summary>The ring of a click grows from this radius to <see cref="RippleEndRadius"/>.</summary>
    public const double RippleStartRadius = 7;
    public const double RippleEndRadius = 30;
    public const double RippleWidth = 2.6;

    public const double PulseStartRadius = 6;
    public const double PulseEndRadius = 26;

    /// <summary>The shadow under the pointer: how far down it falls and how dark it is.</summary>
    public const double ShadowDrop = 1.6;
    public const byte ShadowAlpha = 0x55;

    /// <summary>The ring a right click adds inside its effect, as a share of the effect's radius.</summary>
    public const double SecondaryRingShare = 0.55;

    public const double HaloRadius = 22;
    public const double HaloOpacity = 0.35;
    public const double HoldRadius = 15;

    /// <summary>The circles that go with the pointer, centred on its tip: the halo, and the ring of a button held down.</summary>
    public static IEnumerable<ClickShape> PointerShapes(CursorStyle style, CursorPose pose)
    {
        if (style.Halo)
        {
            yield return new ClickShape(HaloRadius, 0, HaloOpacity * pose.Opacity);
        }
        if (pose.Held > 0)
        {
            yield return new ClickShape(HoldRadius, RippleWidth, pose.Held);
        }
    }

    /// <summary>The colours offered for the click effects and the halo: bright ones, that show on dark and light windows alike.</summary>
    public static Color TintColor(ClickTint tint) => tint switch
    {
        ClickTint.Red => Color.FromRgb(0xFF, 0x4D, 0x4D),
        ClickTint.Yellow => Color.FromRgb(0xFF, 0xC8, 0x3D),
        ClickTint.Green => Color.FromRgb(0x3D, 0xD6, 0x8C),
        ClickTint.White => Colors.White,
        _ => Color.FromRgb(0x4F, 0x9D, 0xFF),
    };

    /// <summary>The circles that make up a click's effect at this point of it.</summary>
    public static IEnumerable<ClickShape> ClickShapes(ClickEffect effect, ClickMark click)
    {
        double growth = ClickGrowth(click.Progress), opacity = ClickOpacity(click.Progress);
        bool pulse = effect == ClickEffect.Pulse;
        double start = pulse ? PulseStartRadius : RippleStartRadius, end = pulse ? PulseEndRadius : RippleEndRadius;
        double radius = start + (end - start) * growth;
        yield return new ClickShape(radius, pulse ? 0 : RippleWidth, pulse ? opacity * 0.45 : opacity);
        if (click.Secondary)
        {
            yield return new ClickShape(radius * SecondaryRingShare, RippleWidth, opacity);
        }
    }

    private static readonly Color Ink = Color.FromRgb(0x14, 0x14, 0x16);

    public static (Color Fill, Color Edge) Paint(CursorShape shape) => shape switch
    {
        CursorShape.LightArrow => (Colors.White, Ink),
        CursorShape.Dot => (Colors.White, Color.FromArgb(0xB0, 0x14, 0x14, 0x16)),
        _ => (Ink, Colors.White),
    };

    /// <summary>How opaque a click's effect is as it goes (0 to 1 along it): it starts strong and fades out.</summary>
    public static double ClickOpacity(double progress) => Math.Pow(1 - Math.Clamp(progress, 0, 1), 1.5);

    /// <summary>How far a click's effect has grown (0 to 1 along it): quick at first, slowing down.</summary>
    public static double ClickGrowth(double progress) => ZoomEasing.Out(Math.Clamp(progress, 0, 1));

    /// <summary>The shape as a WPF geometry, in the same units, for the editor's preview.</summary>
    public static Geometry Geometry(CursorShape shape, CursorKind kind = CursorKind.Arrow)
    {
        if (shape == CursorShape.Dot)
        {
            return new EllipseGeometry(new Point(0, 0), DotRadius, DotRadius);
        }

        Point[] outline = Outline(kind);
        var figure = new PathFigure { StartPoint = outline[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(outline[1..], isStroked: true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
