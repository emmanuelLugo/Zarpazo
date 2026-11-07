namespace Zarpazo.Pin;

/// <summary>The size rules of a pinned capture: how far it can be scaled, and where it ends up when it is.</summary>
internal static class PinScale
{
    private const double Step = 1.1;       // one notch of the wheel
    private const double SnapRange = 0.05; // near 100 % it sticks there, so the text can go back to its original sharpness
    private const double MinSide = 60;     // the shortest side never goes below this many pixels (unless the image already is)
    private const double MaxScale = 4;

    public static double Clamp(double scale, int width, int height)
    {
        double min = Math.Min(1, MinSide / Math.Min(width, height));
        double clamped = Math.Clamp(scale, min, MaxScale);
        return Math.Abs(clamped - 1) < SnapRange ? 1 : clamped;
    }

    /// <summary>The scale after one wheel notch (up makes it larger).</summary>
    public static double Next(double current, int wheelDelta, int width, int height) =>
        Clamp(current * (wheelDelta > 0 ? Step : 1 / Step), width, height);

    /// <summary>
    /// Where the top-left corner goes so that the point at (<paramref name="fractionX"/>, <paramref name="fractionY"/>) of the
    /// picture, as fractions of its size, stays under (<paramref name="anchorX"/>, <paramref name="anchorY"/>).
    /// </summary>
    public static (int Left, int Top) Origin(int anchorX, int anchorY, double fractionX, double fractionY, int width, int height) =>
        ((int)Math.Round(anchorX - fractionX * width), (int)Math.Round(anchorY - fractionY * height));
}
