namespace Zarpazo.Recording;

/// <summary>
/// Where the camera looks at each moment of the edited video. The zooms, kept on the original's clock, are laid on the
/// pieces in their order; each one zooms in at its start and back out at its end, and zooms close to each other are joined:
/// the camera glides from one point to the next instead of zooming out and in again. How fast it moves is the
/// <see cref="ZoomMotion"/> chosen (see <see cref="ZoomTiming"/>). The same rules serve the preview and the saved video, so
/// both look alike.
/// </summary>
internal sealed class ZoomCamera
{
    /// <summary>Bits of a zoom left shorter than this by a cut are dropped: they would only flicker.</summary>
    private const double MinSpanSeconds = 0.25;

    private readonly List<List<ZoomSpan>> _chains = [];
    private readonly ZoomTiming _timing;

    public ZoomCamera(IReadOnlyList<VideoSegment> segments, IReadOnlyList<ZoomRegion> zooms, ZoomMotion motion = ZoomMotion.Smooth)
    {
        _timing = ZoomTiming.For(motion);
        Spans = Place(segments, zooms);
        foreach (ZoomSpan span in Spans)
        {
            if (_chains.Count > 0 && span.Start - _chains[^1][^1].End <= _timing.JoinGapSeconds)
            {
                _chains[^1].Add(span);
            }
            else
            {
                _chains.Add([span]);
            }
        }
    }

    /// <summary>The zooms where they fall in the edited video, in order.</summary>
    public IReadOnlyList<ZoomSpan> Spans { get; }

    public bool IsEmpty => Spans.Count == 0;

    /// <summary>What the edited video shows at <paramref name="time"/> seconds.</summary>
    public ZoomView ViewAt(double time)
    {
        foreach (List<ZoomSpan> chain in _chains)
        {
            double start = chain[0].Start;
            double end = chain[^1].End;
            if (time <= start || time >= end)
            {
                continue;
            }

            double zoomIn = ZoomEasing.Out((time - start) / _timing.InSeconds);
            double zoomOut = ZoomEasing.Out((time - (end - _timing.OutSeconds)) / _timing.OutSeconds);
            double strength = zoomIn * (1 - zoomOut);
            (double scale, double focusX, double focusY) = TargetAt(chain, time);

            // The point is pulled in just enough that the zoomed view stays inside the picture, then the camera moves
            // towards it as it zooms, so the point grows from where it is instead of sliding in from the side.
            double half = 0.5 / scale;
            double centerX = 0.5 + (Math.Clamp(focusX, half, 1 - half) - 0.5) * strength;
            double centerY = 0.5 + (Math.Clamp(focusY, half, 1 - half) - 0.5) * strength;
            return ZoomView.Around(1 + (scale - 1) * strength, centerX, centerY);
        }
        return ZoomView.Full;
    }

    // Inside a chain: the zoom being shown, or a blend of two while gliding from one to the next.
    private (double Scale, double X, double Y) TargetAt(List<ZoomSpan> chain, double time)
    {
        for (int i = 0; i < chain.Count - 1; i++)
        {
            ZoomSpan from = chain[i];
            ZoomSpan to = chain[i + 1];
            double glideStart = Math.Max(from.Start + (from.End - from.Start) / 2, from.End - _timing.GlideHalfSeconds);
            double glideEnd = Math.Min(to.Start + (to.End - to.Start) / 2, to.Start + _timing.GlideHalfSeconds);
            if (time < glideStart)
            {
                return (from.Scale, from.FocusX, from.FocusY);
            }
            if (time < glideEnd)
            {
                double t = ZoomEasing.InOut((time - glideStart) / (glideEnd - glideStart));
                return (Lerp(from.Scale, to.Scale, t), Lerp(from.FocusX, to.FocusX, t), Lerp(from.FocusY, to.FocusY, t));
            }
        }
        ZoomSpan last = chain[^1];
        return (last.Scale, last.FocusX, last.FocusY);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // Each piece shows the part of every zoom that falls inside it, moved to where the piece plays.
    // The zooms come in order of their start (see EditTimeline.Zooms), so the spans of each piece come out in order too.
    private static List<ZoomSpan> Place(IReadOnlyList<VideoSegment> segments, IReadOnlyList<ZoomRegion> zooms)
    {
        var spans = new List<ZoomSpan>();
        double at = 0;
        foreach (VideoSegment segment in segments)
        {
            for (int i = 0; i < zooms.Count; i++)
            {
                ZoomRegion zoom = zooms[i];
                double start = Math.Max(zoom.Start, segment.Start);
                double end = Math.Min(zoom.End, segment.End);
                if (end - start >= MinSpanSeconds)
                {
                    spans.Add(new ZoomSpan(at + start - segment.Start, at + end - segment.Start, zoom.Scale, zoom.FocusX, zoom.FocusY, i));
                }
            }
            at += segment.Length;
        }
        return spans;
    }
}
