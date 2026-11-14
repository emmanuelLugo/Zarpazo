namespace Zarpazo.Recording;

/// <summary>An overlay placed on the edited video: from <see cref="Start"/> to <see cref="End"/> seconds of the result.</summary>
/// <param name="Index">Which of the edit's overlays it is: a cut can show one overlay in several places.</param>
internal readonly record struct OverlaySpan(double Start, double End, int Index)
{
    /// <summary>Bits of an overlay left shorter than this by a cut are not shown on the row.</summary>
    private const double MinSeconds = 0.05;

    /// <summary>Where each overlay falls in the edited video: the part of it that is inside each piece, moved to where that piece plays.</summary>
    public static IReadOnlyList<OverlaySpan> Place(IReadOnlyList<VideoSegment> segments, IReadOnlyList<VideoOverlay> overlays)
    {
        var spans = new List<OverlaySpan>();
        double at = 0;
        foreach (VideoSegment segment in segments)
        {
            for (int i = 0; i < overlays.Count; i++)
            {
                double start = Math.Max(overlays[i].Start, segment.Start);
                double end = Math.Min(overlays[i].End, segment.End);
                if (end - start >= MinSeconds)
                {
                    spans.Add(new OverlaySpan(at + segment.ToPlay(start), at + segment.ToPlay(end), i));
                }
            }
            at += segment.PlayLength;
        }
        return spans;
    }
}
