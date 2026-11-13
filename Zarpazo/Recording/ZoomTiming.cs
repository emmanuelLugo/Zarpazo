namespace Zarpazo.Recording;

/// <summary>The times of the camera's moves for one <see cref="ZoomMotion"/>.</summary>
/// <param name="InSeconds">How long zooming in takes.</param>
/// <param name="OutSeconds">How long zooming back out takes.</param>
/// <param name="JoinGapSeconds">Zooms closer than this stay zoomed in and the camera glides between them.</param>
/// <param name="GlideHalfSeconds">The glide from one zoom to the next starts this long before the first ends and finishes this long after the next starts.</param>
internal readonly record struct ZoomTiming(double InSeconds, double OutSeconds, double JoinGapSeconds, double GlideHalfSeconds)
{
    public static readonly ZoomTiming Smooth = new(1.5, 1.2, 1.5, 0.6);

    public static readonly ZoomTiming Focused = new(0.8, 0.7, 1.0, 0.35);

    public static ZoomTiming For(ZoomMotion motion) => motion == ZoomMotion.Focused ? Focused : Smooth;
}
