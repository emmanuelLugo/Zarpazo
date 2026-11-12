namespace Zarpazo.Recording;

/// <summary>
/// A stretch of the recording shown closer: from <see cref="Start"/> to <see cref="End"/> (seconds of the original, so the
/// zoom stays on its moment when pieces are cut or moved) the picture grows by <see cref="Scale"/> around a point.
/// </summary>
/// <param name="Scale">How much closer: 1.8 shows the picture 1.8 times bigger.</param>
/// <param name="FocusX">The point to look at, from 0 (left edge) to 1 (right edge) of the picture.</param>
/// <param name="FocusY">From 0 (top) to 1 (bottom).</param>
internal readonly record struct ZoomRegion(double Start, double End, double Scale, double FocusX, double FocusY)
{
    public const double DefaultScale = 1.5; // Recordly's choice too: closer than this felt like too much
    public const double MinScale = 1.1;
    public const double MaxScale = 4;

    /// <summary>A zoom is never shorter than this, so it can be seen and grabbed.</summary>
    public const double MinSeconds = 0.5;

    public double Length => End - Start;

    /// <summary>The same zoom with its values in range: the point inside the picture and the scale between the limits.</summary>
    public ZoomRegion Clamped() => this with
    {
        Scale = Math.Clamp(Scale, MinScale, MaxScale),
        FocusX = Math.Clamp(FocusX, 0, 1),
        FocusY = Math.Clamp(FocusY, 0, 1),
    };
}
