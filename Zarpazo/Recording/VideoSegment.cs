namespace Zarpazo.Recording;

/// <summary>One piece of the recording that goes into the edited video: where it starts and ends in the original, in seconds.</summary>
/// <param name="Muted">The piece plays without its sound.</param>
internal readonly record struct VideoSegment(double Start, double End, bool Muted = false)
{
    public double Length => End - Start;
}
