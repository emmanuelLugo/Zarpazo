using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>
/// Something drawn over the video for a while: a blur that hides a part of the screen, a text, or an arrow. Like the zooms it
/// is kept on the original's clock (so it stays on its moment when pieces are cut or moved) and its points in shares of the
/// ORIGINAL recording (so a crop or a frame never moves what it points at); see <see cref="CropArea"/> for where they are
/// turned into the picture's.
/// </summary>
/// <param name="Start">Seconds of the original.</param>
/// <param name="X">First point, from 0 (left) to 1 (right) of the recording: the blur's top left corner, the arrow's tail, the text's top left corner.</param>
/// <param name="Y">From 0 (top) to 1 (bottom).</param>
/// <param name="X2">Second point: the blur's bottom right corner, the arrow's head. A text has none.</param>
/// <param name="Text">What a text says.</param>
/// <param name="Ink">The colour of a text or an arrow; transparent (the default) means the usual one for its kind.</param>
/// <param name="Size">The height of a text's letters or the width of an arrow's line, as a share of the video's height; 0 means the usual one.</param>
internal readonly record struct VideoOverlay(OverlayKind Kind, double Start, double End, double X, double Y, double X2 = 0, double Y2 = 0,
    string Text = "", Color Ink = default, double Size = 0)
{
    /// <summary>An overlay is never shorter than this, so it can be seen and grabbed.</summary>
    public const double MinSeconds = 0.5;

    public const int MaxTextLength = 120;
    public const double MinSize = 0.01;
    public const double MaxSize = 0.2;

    /// <summary>A text or an arrow fades in and out over this long; a blur starts and stops at once, so nothing shows for a moment.</summary>
    public const double FadeSeconds = 0.2;

    private static readonly Color TextInk = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color ArrowInk = Color.FromRgb(0xE5, 0x48, 0x4D);

    public double Length => End - Start;

    /// <summary>The colour to draw with.</summary>
    public Color Paint => Ink.A != 0 ? Ink : Kind == OverlayKind.Arrow ? ArrowInk : TextInk;

    /// <summary>The size to draw with, as a share of the video's height.</summary>
    public double Extent => Size > 0 ? Math.Clamp(Size, MinSize, MaxSize) : Kind == OverlayKind.Arrow ? 0.008 : 0.05;

    /// <summary>How visible it is at a moment of the original: 1 for a blur while it lasts, a ramp at both ends for the rest.</summary>
    public double OpacityAt(double original)
    {
        if (original < Start || original >= End)
        {
            return 0;
        }
        if (Kind == OverlayKind.Blur)
        {
            return 1;
        }
        double fade = Math.Min(FadeSeconds, Length / 2);
        return fade <= 0 ? 1 : Math.Clamp(Math.Min(original - Start, End - original) / fade, 0, 1);
    }

    /// <summary>The same overlay with its values in range: points inside the picture, a blur's corners in order, a size and a text that fit.</summary>
    public VideoOverlay Tidied()
    {
        double x = Math.Clamp(X, 0, 1), y = Math.Clamp(Y, 0, 1);
        double x2 = Math.Clamp(X2, 0, 1), y2 = Math.Clamp(Y2, 0, 1);
        if (Kind == OverlayKind.Blur)
        {
            (x, x2) = (Math.Min(x, x2), Math.Max(x, x2));
            (y, y2) = (Math.Min(y, y2), Math.Max(y, y2));
        }
        string text = Kind == OverlayKind.Text ? (Text ?? "")[..Math.Min((Text ?? "").Length, MaxTextLength)] : "";
        return this with
        {
            X = x, Y = y, X2 = x2, Y2 = y2, Text = text,
            Size = Size > 0 ? Math.Clamp(Size, MinSize, MaxSize) : 0,
            Ink = Kind == OverlayKind.Blur ? default : Ink,
        };
    }

    /// <summary>The same overlay moved by this much, in shares of the recording, kept inside it.</summary>
    public VideoOverlay Moved(double dx, double dy)
    {
        double left = Math.Min(X, X2), right = Math.Max(X, X2), top = Math.Min(Y, Y2), bottom = Math.Max(Y, Y2);
        if (Kind == OverlayKind.Text)
        {
            left = right = X;
            top = bottom = Y;
        }
        dx = Math.Clamp(dx, -left, 1 - right);
        dy = Math.Clamp(dy, -top, 1 - bottom);
        return this with { X = X + dx, Y = Y + dy, X2 = Kind == OverlayKind.Text ? X2 : X2 + dx, Y2 = Kind == OverlayKind.Text ? Y2 : Y2 + dy };
    }
}
