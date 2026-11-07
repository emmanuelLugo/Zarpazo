using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>How a sound level is drawn, the same in the recording panel and in the recording bar.</summary>
internal static class AudioLevelDisplay
{
    private static readonly Brush Quiet = Frozen(0x66, 0xBB, 0x6A);
    private static readonly Brush Loud = Frozen(0xFF, 0xCA, 0x28);
    private static readonly Brush Clipping = Frozen(0xEF, 0x53, 0x50);

    /// <summary>How much of the bar to fill (0 to 1). Square root: quiet speech is far below full scale and would barely show on a straight scale.</summary>
    public static double Fraction(float level) => Math.Sqrt(Math.Clamp(level, 0f, 1f));

    /// <summary>Green while there is room to spare, amber when it is loud, red when it is about to distort.</summary>
    public static Brush Color(double fraction) => fraction >= 0.9 ? Clipping : fraction >= 0.7 ? Loud : Quiet;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
