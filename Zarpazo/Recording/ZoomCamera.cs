namespace Zarpazo.Recording;

/// <summary>
/// Where the camera looks at each moment of the edited video. The zooms, kept on the original's clock, are laid on the
/// pieces in their order; each one zooms in at its start and back out at its end, and zooms close to each other are joined:
/// the camera glides from one point to the next instead of zooming out and in again. How fast it moves is the
/// <see cref="ZoomMotion"/> chosen (see <see cref="ZoomTiming"/>). Given the recorded pointer, the camera also moves along
/// when the pointer would leave the view. The same rules serve the preview and the saved video, so both look alike.
/// </summary>
internal sealed class ZoomCamera
{
    /// <summary>Bits of a zoom left shorter than this by a cut are dropped: they would only flicker.</summary>
    private const double MinSpanSeconds = 0.25;

    /// <summary>How far from the middle of the view the pointer may get, as a share of half the view, before the camera follows.</summary>
    public const double FollowReach = 0.6;

    // The camera follows with this time constant, forwards and backwards: it sets off a moment early instead of lagging.
    private const double FollowSeconds = 0.2;

    private readonly List<List<ZoomSpan>> _chains = [];
    private readonly List<(double[] X, double[] Y)> _follow = []; // how far each chain moves off its zooms' points, over time
    private readonly ZoomTiming _timing;

    /// <param name="connect">False: every zoom goes back out before the next one, however close they are.</param>
    /// <param name="pointer">Where the pointer was at a moment of the original, in shares of the same picture as the zooms'
    /// points, null when it was not on it; none, and the camera only looks where the zooms say.</param>
    public ZoomCamera(IReadOnlyList<VideoSegment> segments, IReadOnlyList<ZoomRegion> zooms, ZoomMotion motion = ZoomMotion.Smooth, bool connect = true,
        Func<double, (double X, double Y)?>? pointer = null)
    {
        _timing = ZoomTiming.For(motion);
        Spans = Place(segments, zooms);
        foreach (ZoomSpan span in Spans)
        {
            if (connect && _chains.Count > 0 && span.Start - _chains[^1][^1].End <= _timing.JoinGapSeconds)
            {
                _chains[^1].Add(span);
            }
            else
            {
                _chains.Add([span]);
            }
        }
        if (pointer != null)
        {
            foreach (List<ZoomSpan> chain in _chains)
            {
                _follow.Add(Follow(chain, segments, pointer));
            }
        }
    }

    /// <summary>The zooms where they fall in the edited video, in order.</summary>
    public IReadOnlyList<ZoomSpan> Spans { get; }

    public bool IsEmpty => Spans.Count == 0;

    /// <summary>What the edited video shows at <paramref name="time"/> seconds.</summary>
    public ZoomView ViewAt(double time)
    {
        for (int i = 0; i < _chains.Count; i++)
        {
            List<ZoomSpan> chain = _chains[i];
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
            (double moveX, double moveY) = _follow.Count > 0 ? (At(_follow[i].X, time - start), At(_follow[i].Y, time - start)) : (0, 0);
            double centerX = 0.5 + (Math.Clamp(Math.Clamp(focusX, half, 1 - half) + moveX, half, 1 - half) - 0.5) * strength;
            double centerY = 0.5 + (Math.Clamp(Math.Clamp(focusY, half, 1 - half) + moveY, half, 1 - half) - 0.5) * strength;
            return ZoomView.Around(1 + (scale - 1) * strength, centerX, centerY);
        }
        return ZoomView.Full;
    }

    // At each step of the chain, how far the view has to move from the zoom's point to keep the pointer within reach of its
    // middle (nothing while it is, or while there is no pointer), then smoothed. Measured from the zoom's point each time,
    // so the camera comes back when the pointer does and never drifts. Until the pointer has been in what the zoom shows
    // there is nothing to follow: it is still on its way there (or the zoom looks at something else on purpose).
    private (double[] X, double[] Y) Follow(List<ZoomSpan> chain, IReadOnlyList<VideoSegment> segments, Func<double, (double X, double Y)?> pointer)
    {
        double start = chain[0].Start;
        int count = (int)Math.Ceiling((chain[^1].End - start) / CursorMotion.StepSeconds) + 1;
        var x = new double[count];
        var y = new double[count];
        bool arrived = false;
        for (int i = 0; i < count; i++)
        {
            double time = start + i * CursorMotion.StepSeconds;
            if (pointer(CursorLayer.ToOriginal(segments, time)) is not { } at)
            {
                continue;
            }
            (double scale, double focusX, double focusY) = TargetAt(chain, time);
            double half = 0.5 / scale;
            arrived |= Math.Abs(at.X - Math.Clamp(focusX, half, 1 - half)) <= half && Math.Abs(at.Y - Math.Clamp(focusY, half, 1 - half)) <= half;
            if (!arrived)
            {
                continue;
            }
            x[i] = Move(focusX, at.X, half);
            y[i] = Move(focusY, at.Y, half);
        }
        CursorMotion.SmoothBothWays(x, FollowSeconds);
        CursorMotion.SmoothBothWays(y, FollowSeconds);
        return (x, y);

        static double Move(double focus, double at, double half)
        {
            double anchor = Math.Clamp(focus, half, 1 - half);
            double reach = half * FollowReach;
            return Math.Clamp(Math.Clamp(anchor, at - reach, at + reach), half, 1 - half) - anchor;
        }
    }

    private static double At(double[] values, double seconds) =>
        values[Math.Clamp((int)Math.Round(seconds / CursorMotion.StepSeconds), 0, values.Length - 1)];

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
                    spans.Add(new ZoomSpan(at + segment.ToPlay(start), at + segment.ToPlay(end), zoom.Scale, zoom.FocusX, zoom.FocusY, i));
                }
            }
            at += segment.PlayLength;
        }
        return spans;
    }
}
