namespace Zarpazo.Recording;

/// <summary>
/// The path of the drawn pointer, worked out once from the recorded track: smoothed, leaning as it moves, and with its clicks.
/// The recorded positions come one per picture and in steps; here they are laid on an even clock and smoothed forwards and
/// backwards, which removes the shake of the hand without the lag a live filter has: the pointer still reaches what it
/// clicks at the moment of the click. The same path serves the preview and the saved video. Times are the original's.
/// </summary>
internal sealed class CursorMotion
{
    /// <summary>How finely the path is worked out.</summary>
    public const double StepSeconds = 1.0 / 120;

    /// <summary>How long the effect of a click lasts.</summary>
    public const double ClickSeconds = 0.45;

    /// <summary>How long the pointer takes to shrink and come back after a click.</summary>
    public const double BounceSeconds = 0.3;

    /// <summary>How much smaller the pointer gets at the bottom of the bounce.</summary>
    public const double BounceDepth = 0.22;

    /// <summary>The most the arrow leans, in degrees, at full sway.</summary>
    public const double MaxAngle = 24;

    // At full smoothing each pass follows the hand with this time constant.
    private const double MaxGlideSeconds = 0.08;

    private const double TailSeconds = 1;

    // How far before and after a click the path is bent onto the clicked point.
    private const double PinSeconds = 0.25;

    // A move recorded after the pointer stood still happened within about a picture before it, not spread over the wait.
    private const double MoveSeconds = 0.05;

    // How quickly the lean answers to speed (speed in picture heights per second), and how long it takes to settle.
    private const double LeanPerSpeed = 0.6;
    private const double LeanSeconds = 0.08;

    private readonly CursorStyle _style;
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _angle;
    private readonly (double Time, double X, double Y)[] _clicks;

    public CursorMotion(CursorTrack track, CursorStyle style)
    {
        _style = style;
        IReadOnlyList<CursorSample> moves = track.Moves;
        if (moves.Count == 0)
        {
            _x = _y = _angle = [];
            _clicks = [];
            return;
        }

        // A still second after the last move, so the smoothing and the lean settle where the pointer stopped.
        int count = (int)Math.Ceiling((moves[^1].TimeMs / 1000.0 + TailSeconds) / StepSeconds) + 1;
        _x = new double[count];
        _y = new double[count];
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            double time = i * StepSeconds;
            while (at + 1 < moves.Count && moves[at + 1].TimeMs / 1000.0 <= time)
            {
                at++;
            }

            double x = moves[at].X, y = moves[at].Y;
            if (at + 1 < moves.Count && time >= moves[at].TimeMs / 1000.0)
            {
                CursorSample next = moves[at + 1];
                double end = next.TimeMs / 1000.0;
                double start = Math.Max(moves[at].TimeMs / 1000.0, end - MoveSeconds);
                if (time > start)
                {
                    double share = (time - start) / (end - start);
                    x += (next.X - x) * share;
                    y += (next.Y - y) * share;
                }
            }
            _x[i] = x / track.Width;
            _y[i] = y / track.Height;
        }

        double glide = style.Smoothing * MaxGlideSeconds;
        if (glide > 0)
        {
            double[] rawX = (double[])_x.Clone(), rawY = (double[])_y.Clone();
            SmoothBothWays(_x, glide);
            SmoothBothWays(_y, glide);
            foreach (CursorClick click in track.Clicks)
            {
                PinAt(_x, rawX, click.TimeMs / 1000.0);
                PinAt(_y, rawY, click.TimeMs / 1000.0);
            }
        }

        _angle = Lean(_x, track.Width / (double)track.Height, style.Sway);
        _clicks = track.Clicks
            .Select(click => click.TimeMs / 1000.0)
            .Select(time => (time, Sample(_x, time), Sample(_y, time)))
            .ToArray();
    }

    // The same path in another style: only the smoothing and the sway shape the path, so for any other change (size, shape,
    // click effect, bounce) it is not worked out again. The editor's sliders call this at every step.
    private CursorMotion(CursorMotion path, CursorStyle style)
    {
        _style = style;
        _x = path._x;
        _y = path._y;
        _angle = path._angle;
        _clicks = path._clicks;
    }

    /// <summary>This path in <paramref name="style"/>; worked out again only if the smoothing or the sway differ.</summary>
    public CursorMotion Restyled(CursorTrack track, CursorStyle style) =>
        style.Smoothing == _style.Smoothing && style.Sway == _style.Sway ? new CursorMotion(this, style) : new CursorMotion(track, style);

    /// <summary>The pointer at <paramref name="seconds"/> of the original; null when it is not drawn (switched off, or off the picture).</summary>
    public CursorPose? PoseAt(double seconds)
    {
        if (!_style.Visible || _x.Length == 0)
        {
            return null;
        }

        double x = Sample(_x, seconds), y = Sample(_y, seconds);
        if (x < 0 || x > 1 || y < 0 || y > 1)
        {
            return null;
        }

        double scale = 1;
        if (_style.Bounce)
        {
            foreach ((double time, _, _) in _clicks)
            {
                double share = (seconds - time) / BounceSeconds;
                if (share is >= 0 and <= 1)
                {
                    scale = Math.Min(scale, 1 - BounceDepth * Math.Sin(Math.PI * share));
                }
            }
        }
        return new CursorPose(x, y, Sample(_angle, seconds), scale);
    }

    /// <summary>The clicks whose effect shows at <paramref name="seconds"/>, oldest first.</summary>
    public IReadOnlyList<ClickMark> ClicksAt(double seconds)
    {
        if (!_style.Visible || _style.Click == ClickEffect.None)
        {
            return [];
        }

        List<ClickMark>? marks = null;
        foreach ((double time, double x, double y) in _clicks)
        {
            double share = (seconds - time) / ClickSeconds;
            if (share is >= 0 and <= 1)
            {
                (marks ??= []).Add(new ClickMark(x, y, share));
            }
        }
        return marks ?? (IReadOnlyList<ClickMark>)[];
    }

    // An exponential follower run forwards and then backwards: each pass lags by as much as the other leads.
    private static void SmoothBothWays(double[] values, double seconds)
    {
        double keep = Math.Exp(-StepSeconds / seconds);
        for (int i = 1; i < values.Length; i++)
        {
            values[i] = values[i - 1] * keep + values[i] * (1 - keep);
        }
        for (int i = values.Length - 2; i >= 0; i--)
        {
            values[i] = values[i + 1] * keep + values[i] * (1 - keep);
        }
    }

    // Smoothing rounds off a quick move that ends on a button, and the pointer could still be a few pixels short when it
    // clicks. Around each click the path is bent onto the recorded point, fully at the click and less and less away from it.
    private static void PinAt(double[] smooth, double[] raw, double seconds)
    {
        int center = (int)Math.Round(seconds / StepSeconds);
        if (center < 0 || center >= smooth.Length)
        {
            return;
        }

        double offset = raw[center] - smooth[center];
        int reach = (int)Math.Round(PinSeconds / StepSeconds);
        for (int i = Math.Max(0, center - reach); i <= Math.Min(smooth.Length - 1, center + reach); i++)
        {
            double weight = 0.5 + 0.5 * Math.Cos(Math.PI * (i - center) / reach);
            smooth[i] += offset * weight;
        }
    }

    // Moving right, the body of the arrow trails to the left behind its tip, which is a clockwise turn; it settles a moment
    // after the speed changes, like something hanging from the tip.
    private static double[] Lean(double[] x, double aspect, double sway)
    {
        var angle = new double[x.Length];
        if (sway <= 0)
        {
            return angle;
        }

        double keep = Math.Exp(-StepSeconds / LeanSeconds);
        double current = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double before = x[Math.Max(0, i - 1)], after = x[Math.Min(x.Length - 1, i + 1)];
            double span = (Math.Min(x.Length - 1, i + 1) - Math.Max(0, i - 1)) * StepSeconds;
            double speed = span > 0 ? (after - before) * aspect / span : 0; // picture heights per second
            double target = MaxAngle * sway * Math.Tanh(speed * LeanPerSpeed);
            current = current * keep + target * (1 - keep);
            angle[i] = current;
        }
        return angle;
    }

    // Between the two worked-out points around the moment; before the start or after the end, the nearest one.
    private static double Sample(double[] values, double seconds)
    {
        double index = seconds / StepSeconds;
        if (index <= 0)
        {
            return values[0];
        }
        if (index >= values.Length - 1)
        {
            return values[^1];
        }

        int below = (int)index;
        double share = index - below;
        return values[below] + (values[below + 1] - values[below]) * share;
    }
}
