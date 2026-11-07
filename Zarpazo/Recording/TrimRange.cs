namespace Zarpazo.Recording;

/// <summary>The part of a video that is kept: where it starts and ends, always at least a little long.</summary>
internal static class TrimRange
{
    public const double MinSeconds = 0.5;

    /// <summary>
    /// Brings a start and an end inside the video and keeps them at least <see cref="MinSeconds"/> apart, by moving
    /// whichever of the two the person is not dragging.
    /// </summary>
    /// <param name="movingStart">True when the start is the one being dragged; false when it is the end.</param>
    public static (double Start, double End) Clamp(double start, double end, double duration, bool movingStart)
    {
        if (duration <= MinSeconds)
        {
            return (0, Math.Max(0, duration)); // too short to cut at all
        }

        start = Math.Clamp(start, 0, duration);
        end = Math.Clamp(end, 0, duration);
        if (end - start < MinSeconds)
        {
            if (movingStart)
            {
                start = Math.Max(0, end - MinSeconds);
                end = Math.Max(end, start + MinSeconds);
            }
            else
            {
                end = Math.Min(duration, start + MinSeconds);
                start = Math.Min(start, end - MinSeconds);
            }
        }
        return (start, end);
    }
}
