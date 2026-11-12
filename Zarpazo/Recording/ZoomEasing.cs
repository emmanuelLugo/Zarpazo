namespace Zarpazo.Recording;

/// <summary>The curves the zoom moves along, so it starts quickly and settles softly instead of moving at a constant speed.</summary>
internal static class ZoomEasing
{
    /// <summary>Fast at first and gentle at the end: for zooming in and out.</summary>
    public static double Out(double t) => CubicBezier(0.16, 1, 0.3, 1, t);

    /// <summary>Gentle at both ends: for moving the camera from one zoom to the next.</summary>
    public static double InOut(double t) => CubicBezier(0.65, 0, 0.35, 1, t);

    /// <summary>The CSS cubic-bezier curve through (0,0), (x1,y1), (x2,y2), (1,1), read at <paramref name="t"/> (0 to 1).</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double t)
    {
        if (t <= 0)
        {
            return 0;
        }
        if (t >= 1)
        {
            return 1;
        }

        // Find the curve's own parameter where its x equals t (x grows with it), then read y there.
        double low = 0, high = 1, u = t;
        for (int i = 0; i < 30; i++)
        {
            u = (low + high) / 2;
            if (Bezier(x1, x2, u) < t)
            {
                low = u;
            }
            else
            {
                high = u;
            }
        }
        return Bezier(y1, y2, u);
    }

    private static double Bezier(double p1, double p2, double u)
    {
        double v = 1 - u;
        return 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u;
    }
}
