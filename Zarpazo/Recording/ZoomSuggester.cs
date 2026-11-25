namespace Zarpazo.Recording;

/// <summary>
/// Proposes zooms from the clicks of a recording: clicks close in time and in place make one zoom, centred on them, that
/// starts a little before the first and ends a little after the last. Clicks that jump about faster than the camera can
/// settle get no zoom: they are seen on the whole picture.
/// </summary>
internal static class ZoomSuggester
{
    /// <summary>A click this soon after the previous one (counting from when its button came up) joins its zoom.</summary>
    public const double MergeGapSeconds = 2.5;

    /// <summary>The zoom starts this long before the first click, so the camera is nearly there when it happens, and ends
    /// this long after the last.</summary>
    public const double LeadSeconds = 0.8;
    public const double PadSeconds = 0.5;

    /// <summary>A zoom for a single click lasts at least this long, so it is not over before it is seen.</summary>
    public const double MinSeconds = 2.5; // with a gentler zoom in and out, a shorter one never settles

    // When the next click comes before a zoom is over, that zoom gives way this long before the click, so the camera is
    // there when it happens (see ZoomTiming.GlideHalfSeconds); it still stays a moment after its own last click.
    private const double ArriveSeconds = 0.6;
    private const double LingerSeconds = 0.2;

    // A group of clicks left for a faraway one sooner than this after it began is not zoomed in on: the camera would only
    // swing from one side to the other. The whole picture shows for a moment around such clicks, longer than half the gap
    // that joins two zooms (see ZoomTiming.JoinGapSeconds), so the camera does go all the way out.
    private const double SettleSeconds = 2;
    private const double ClearSeconds = 0.8;
    private const double MinUsefulSeconds = 1.5; // a zoom squeezed shorter than it takes to go in is left out

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

        List<(double First, double Up, double FocusX, double FocusY)> groups = Group(clicks, track, scale);
        bool Zoomed(int i) => i == groups.Count - 1 || groups[i + 1].First - groups[i].First >= SettleSeconds;

        var zooms = new List<ZoomRegion>();
        double previousUp = 0; // when the button of the last zoom's last click came up
        for (int i = 0; i < groups.Count; i++)
        {
            if (!Zoomed(i))
            {
                continue;
            }

            (double first, double lastUp, double focusX, double focusY) = groups[i];
            double start = first - LeadSeconds;
            double end = lastUp + PadSeconds;
            if (end - start < MinSeconds)
            {
                // Centred on the group, then pushed back inside the recording.
                double middle = (start + end) / 2;
                (start, end) = (middle - MinSeconds / 2, middle + MinSeconds / 2);
            }
            (start, end) = ShiftInside(start, end, duration);

            bool squeezed = false;
            if (i > 0 && !Zoomed(i - 1) && start < groups[i - 1].Up + ClearSeconds)
            {
                (start, squeezed) = (groups[i - 1].Up + ClearSeconds, true);
                end = Math.Max(end, Math.Min(duration, start + MinSeconds)); // what is lost at the start is made up after
            }
            if (i + 1 < groups.Count && !Zoomed(i + 1))
            {
                double leave = Math.Max(groups[i + 1].First - ClearSeconds, lastUp + LingerSeconds);
                (end, squeezed) = (Math.Min(end, leave), squeezed || leave < end);
            }
            if (zooms.Count > 0 && start < zooms[^1].End)
            {
                // One right after the other: the camera moves across without zooming out.
                double change = Math.Max(first - ArriveSeconds, Math.Max(previousUp + LingerSeconds, zooms[^1].Start + ZoomRegion.MinSeconds));
                start = Math.Min(change, zooms[^1].End);
                zooms[^1] = zooms[^1] with { End = start };
            }

            if (end - start >= (squeezed ? MinUsefulSeconds : ZoomRegion.MinSeconds))
            {
                zooms.Add(new ZoomRegion(start, end, scale, focusX, focusY).Clamped());
                previousUp = lastUp;
            }
        }
        return zooms;
    }

    // Each group grows while the next click is soon enough and still fits in the view around the others.
    private static List<(double First, double Up, double FocusX, double FocusY)> Group(List<CursorClick> clicks, CursorTrack track, double scale)
    {
        var groups = new List<(double First, double Up, double FocusX, double FocusY)>();
        double maxWidth = track.Width / scale * FitShare;
        double maxHeight = track.Height / scale * FitShare;
        int first = 0;
        while (first < clicks.Count)
        {
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

            groups.Add((clicks[first].TimeMs / 1000.0, lastUp, (left + right) / 2.0 / track.Width, (top + bottom) / 2.0 / track.Height));
            first = next;
        }
        return groups;
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
