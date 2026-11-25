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

    /// <summary>The colours offered for the click effects: bright ones, that show on dark and light windows alike.</summary>
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
    public static Geometry Geometry(CursorShape shape)
    {
        if (shape == CursorShape.Dot)
        {
            return new EllipseGeometry(new Point(0, 0), DotRadius, DotRadius);
        }

        var figure = new PathFigure { StartPoint = Arrow[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(Arrow[1..], isStroked: true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
