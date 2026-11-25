namespace Zarpazo.Recording;

/// <summary>
/// Proposes zooms from the clicks of a recording: clicks close in time and in place make one zoom, centred on them, that
/// starts a little before the first and ends a little after the last.
/// </summary>
internal static class ZoomSuggester
{
    /// <summary>A click this soon after the previous one (counting from when its button came up) joins its zoom.</summary>
    public const double MergeGapSeconds = 2.5;

    /// <summary>The zoom starts this long before the first click and ends this long after the last.</summary>
    public const double PadSeconds = 0.5;

    /// <summary>A zoom for a single click lasts at least this long, so it is not over before it is seen.</summary>
    public const double MinSeconds = 2.5; // with a gentler zoom in and out, a shorter one never settles

    // When the next click comes before a zoom is over, that zoom gives way this long before the click, so the camera is
    // there when it happens (see ZoomTiming.GlideHalfSeconds); it still stays a moment after its own last click.
    private const double ArriveSeconds = 0.6;
    private const double LingerSeconds = 0.2;

    /// <summary>Clicks of one zoom must fit in this share of what the zoom shows, so none is left at the edge.</summary>
    private const double FitShare = 0.8;

    /// <param name="duration">The length of the recording in seconds.</param>
    /// <returns>The zooms in order, none overlapping another.</returns>
    public static IReadOnlyList<ZoomRegion> Suggest(CursorTrack track, double duration, double scale = ZoomRegion.DefaultScale)
    {
        // Clicks outside the picture (on the recording bar, on another screen) are not part of the video.
        List<CursorClick> clicks = track.Clicks
            .Where(click => click.X >= 0 && click.Y >= 0 && click.X < track.Width && click.Y < track.Height)
            .Where(click => click.TimeMs / 1000.0 < duration)
            .OrderBy(click => click.TimeMs)
            .ToList();

        var zooms = new List<ZoomRegion>();
        double previousUp = 0; // when the button of the last zoom's last click came up
        double maxWidth = track.Width / scale * FitShare;
        double maxHeight = track.Height / scale * FitShare;
        int first = 0;
        while (first < clicks.Count)
        {
            // The group grows while the next click is soon enough and still fits in the view around the others.
            double lastUp = clicks[first].EndMs / 1000.0;
            (int left, int top, int right, int bottom) = (clicks[first].X, clicks[first].Y, clicks[first].X, clicks[first].Y);
            int next = first + 1;
            for (; next < clicks.Count; next++)
            {
                CursorClick click = clicks[next];
                int newLeft = Math.Min(left, click.X), newRight = Math.Max(right, click.X);
                int newTop = Math.Min(top, click.Y), newBottom = Math.Max(bottom, click.Y);
                if (click.TimeMs / 1000.0 - lastUp > MergeGapSeconds || newRight - newLeft > maxWidth || newBottom - newTop > maxHeight)
                {
                    break;
                }
                (left, top, right, bottom) = (newLeft, newTop, newRight, newBottom);
                lastUp = Math.Max(lastUp, click.EndMs / 1000.0);
            }

            double start = clicks[first].TimeMs / 1000.0 - PadSeconds;
            double end = lastUp + PadSeconds;
            if (end - start < MinSeconds)
            {
                // Centred on the group, then pushed back inside the recording.
                double middle = (start + end) / 2;
                (start, end) = (middle - MinSeconds / 2, middle + MinSeconds / 2);
            }
            (start, end) = ShiftInside(start, end, duration);
            if (zooms.Count > 0 && start < zooms[^1].End)
            {
                // One right after the other: the camera moves across without zooming out.
                double change = Math.Max(clicks[first].TimeMs / 1000.0 - ArriveSeconds,
                    Math.Max(previousUp + LingerSeconds, zooms[^1].Start + ZoomRegion.MinSeconds));
                start = Math.Min(change, zooms[^1].End);
                zooms[^1] = zooms[^1] with { End = start };
            }

            if (end - start >= ZoomRegion.MinSeconds)
            {
                double focusX = (left + right) / 2.0 / track.Width;
                double focusY = (top + bottom) / 2.0 / track.Height;
                zooms.Add(new ZoomRegion(start, end, scale, focusX, focusY).Clamped());
                previousUp = lastUp;
            }
            first = next;
        }
        return zooms;
    }

    private static (double Start, double End) ShiftInside(double start, double end, double duration)
    {
        if (start < 0)
        {
            (start, end) = (0, end - start);
        }
        if (end > duration)
        {
            (start, end) = (Math.Max(0, start - (end - duration)), duration);
        }
        return (start, end);
    }
}
