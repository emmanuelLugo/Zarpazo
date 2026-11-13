namespace Zarpazo.Recording;

/// <summary>
/// How much of each side of the recorded picture is cut away, like Recordly's crop: the taskbar, a browser's tabs, the edge
/// of a window. Each side is a share of the picture's size (0 keeps it all). The rest fills the finished video, which takes
/// its size and proportions. It belongs to one video, so it is part of the edit (undone with it) and never of the settings.
/// </summary>
/// <param name="Left">Cut from the left edge, as a share of the picture's width.</param>
/// <param name="Top">Cut from the top, as a share of its height.</param>
internal readonly record struct VideoCrop(double Left, double Top, double Right, double Bottom)
{
    /// <summary>At least this share of the picture is kept across and down.</summary>
    public const double MinKept = 0.1;

    public static readonly VideoCrop None = new(0, 0, 0, 0);

    public bool IsNone => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;

    /// <summary>The share of the picture's width that is kept.</summary>
    public double Width => 1 - Left - Right;

    public double Height => 1 - Top - Bottom;

    /// <summary>One side moved to <paramref name="value"/>; it stops where the side across would leave too little.</summary>
    public VideoCrop With(CropEdges edge, double value)
    {
        value = double.IsFinite(value) ? Math.Max(0, value) : 0;
        return edge switch
        {
            CropEdges.Left => this with { Left = Math.Min(value, 1 - MinKept - Right) },
            CropEdges.Top => this with { Top = Math.Min(value, 1 - MinKept - Bottom) },
            CropEdges.Right => this with { Right = Math.Min(value, 1 - MinKept - Left) },
            CropEdges.Bottom => this with { Bottom = Math.Min(value, 1 - MinKept - Top) },
            _ => this,
        };
    }

    /// <summary>The same crop with its sides in range and enough of the picture left (an odd value from anywhere).</summary>
    public VideoCrop Tidied()
    {
        static double Side(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1 - MinKept) : 0;
        var crop = new VideoCrop(Side(Left), Side(Top), Side(Right), Side(Bottom));
        // Too much cut across: both sides give way in proportion, so the middle of what was meant stays.
        if (crop.Width < MinKept - 1e-9)
        {
            double scale = (1 - MinKept) / (crop.Left + crop.Right);
            crop = crop with { Left = crop.Left * scale, Right = crop.Right * scale };
        }
        if (crop.Height < MinKept - 1e-9)
        {
            double scale = (1 - MinKept) / (crop.Top + crop.Bottom);
            crop = crop with { Top = crop.Top * scale, Bottom = crop.Bottom * scale };
        }
        return crop;
    }
}
