namespace Zarpazo.Recording;

/// <summary>The curves the camera moves along, so it never starts or stops with a jolt.</summary>
internal static class ZoomEasing
{
    private const double Stiffness = 7.4; // how quickly the spring settles: at the end it is within half a percent

    private static readonly double SpringEnd = 1 - (1 + Stiffness) * Math.Exp(-Stiffness);

    /// <summary>
    /// Like a camera on a spring that is just stiff enough not to bounce: it starts from rest, speeds up, and settles gently
    /// into place. For zooming in and out. (A curve that is fast from the first instant felt like a jump.)
    /// </summary>
    public static double Out(double t)
    {
        if (t <= 0)
        {
            return 0;
        }
        if (t >= 1)
        {
            return 1;
        }
        double k = Stiffness * t;
        return (1 - (1 + k) * Math.Exp(-k)) / SpringEnd;
    }

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
