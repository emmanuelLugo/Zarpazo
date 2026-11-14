namespace Zarpazo.Recording;

/// <summary>
/// The outline of an arrow: a line that stops inside a triangular head. One drawing for the preview and for the saved video,
/// so they look alike. Points are in the pixels of the picture the arrow is drawn on.
/// </summary>
internal readonly record struct ArrowShape((double X, double Y) Tail, (double X, double Y) Neck, (double X, double Y) Tip,
    (double X, double Y) Left, (double X, double Y) Right)
{
    private const double HeadLength = 4.2;   // in line widths
    private const double HeadHalfAngle = 0.5; // radians
    private const double NeckShare = 0.7;     // how far into the head the line goes

    /// <summary>The arrow from <paramref name="tail"/> to <paramref name="tip"/> with a line <paramref name="thickness"/> wide; null when it is too short to see.</summary>
    public static ArrowShape? For((double X, double Y) tail, (double X, double Y) tip, double thickness)
    {
        double length = Math.Sqrt(Math.Pow(tip.X - tail.X, 2) + Math.Pow(tip.Y - tail.Y, 2));
        if (length < 1)
        {
            return null;
        }

        double angle = Math.Atan2(tip.Y - tail.Y, tip.X - tail.X);
        double head = Math.Min(thickness * HeadLength, length * 0.6);
        (double, double) Back(double length, double turn) => (tip.X - length * Math.Cos(angle + turn), tip.Y - length * Math.Sin(angle + turn));
        return new ArrowShape(tail, Back(head * NeckShare, 0), tip, Back(head, HeadHalfAngle), Back(head, -HeadHalfAngle));
    }
}
