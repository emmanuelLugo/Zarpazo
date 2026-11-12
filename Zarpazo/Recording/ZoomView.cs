namespace Zarpazo.Recording;

/// <summary>
/// What a picture of the edited video shows: a rectangle of the recorded picture, in shares of its size (0 to 1), that is
/// stretched to fill the whole frame. The full picture is (0, 0, 1, 1).
/// </summary>
internal readonly record struct ZoomView(double X, double Y, double Width, double Height)
{
    public static readonly ZoomView Full = new(0, 0, 1, 1);

    /// <summary>How much bigger things look: 1 for the full picture.</summary>
    public double Scale => 1 / Width;

    public bool IsFull => Width >= 1 - 1e-6;

    /// <summary>The view of <paramref name="scale"/> centred as close to (<paramref name="centerX"/>, <paramref name="centerY"/>) as the picture's edges allow.</summary>
    public static ZoomView Around(double scale, double centerX, double centerY)
    {
        double size = 1 / Math.Max(1, scale);
        double x = Math.Clamp(centerX - size / 2, 0, 1 - size);
        double y = Math.Clamp(centerY - size / 2, 0, 1 - size);
        return new ZoomView(x, y, size, size);
    }
}
