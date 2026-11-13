using System.IO;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class CursorMotionTests
{
    private static readonly CursorStyle Exact = CursorStyle.Default with { Smoothing = 0, Sway = 0, Bounce = false };

    private static CursorTrack Track(IEnumerable<CursorSample> moves, params CursorClick[] clicks) =>
        new(1000, 500, moves.ToList(), clicks, PointerInPicture: false);

    // One sample per picture at 30 fps, from a function of the time in seconds.
    private static IEnumerable<CursorSample> Every33Ms(double seconds, Func<double, (double X, double Y)> at)
    {
        for (long ms = 0; ms <= seconds * 1000; ms += 33)
        {
            (double x, double y) = at(ms / 1000.0);
            yield return new CursorSample(ms, (int)Math.Round(x), (int)Math.Round(y));
        }
    }

    [Fact]
    public void Without_smoothing_the_pointer_is_where_it_was_recorded()
    {
        var motion = new CursorMotion(Track([new(0, 100, 50), new(500, 400, 250)]), Exact);

        CursorPose start = motion.PoseAt(0)!.Value;
        CursorPose end = motion.PoseAt(0.5)!.Value;

        Assert.Equal(0.1, start.X, 3);
        Assert.Equal(0.1, start.Y, 3);
        Assert.Equal(0.4, end.X, 3);
        Assert.Equal(0.5, end.Y, 3);
    }

    [Fact]
    public void A_move_after_the_pointer_stood_still_is_not_spread_over_the_wait()
    {
        // Still for two seconds, then one picture later somewhere else.
        var motion = new CursorMotion(Track([new(0, 100, 100), new(2000, 900, 100)]), Exact);

        Assert.Equal(0.1, motion.PoseAt(1.0)!.Value.X, 3);
        Assert.Equal(0.1, motion.PoseAt(1.9)!.Value.X, 3);
        Assert.Equal(0.9, motion.PoseAt(2.0)!.Value.X, 3);
    }

    [Fact]
    public void Smoothing_takes_out_the_shake_of_the_hand()
    {
        // Along a straight line, with the hand shaking 6 pixels up and down every picture.
        CursorSample[] moves = Every33Ms(2, t => (100 + 300 * t, 250 + ((int)(t * 30) % 2 == 0 ? 6 : -6))).ToArray();
        var shaky = new CursorMotion(Track(moves), Exact);
        var smooth = new CursorMotion(Track(moves), Exact with { Smoothing = 1 });

        static double Shake(CursorMotion motion)
        {
            double worst = 0;
            for (double t = 0.3; t < 1.7; t += 0.01)
            {
                worst = Math.Max(worst, Math.Abs(motion.PoseAt(t)!.Value.Y * 500 - 250));
            }
            return worst;
        }

        Assert.True(Shake(shaky) > 5);
        Assert.True(Shake(smooth) < 1.5, $"still shakes {Shake(smooth):F2} px");
    }

    [Fact]
    public void Smoothing_does_not_make_the_pointer_lag_behind()
    {
        // A steady move to the right: a live filter would trail behind; this one stays on the line.
        CursorSample[] moves = Every33Ms(2, t => (100 + 400 * t, 250)).ToArray();
        var motion = new CursorMotion(Track(moves), Exact with { Smoothing = 1 });

        Assert.Equal(100 + 400 * 1.0, motion.PoseAt(1.0)!.Value.X * 1000, 0); // within a pixel
    }

    [Fact]
    public void A_smoothed_pointer_reaches_what_it_clicks_by_the_time_of_the_click()
    {
        // A quick move to a button, a short rest on it, and a click.
        var moves = Every33Ms(0.3, t => (100 + 2000 * t, 250)).ToList(); // ends at 700
        moves.Add(new CursorSample(330, 700, 250));
        var click = new CursorClick(500, 560, 700, 250, ClickButton.Left);
        var motion = new CursorMotion(Track(moves, click), Exact with { Smoothing = 1 });

        Assert.InRange(motion.PoseAt(0.5)!.Value.X * 1000, 697, 703);
    }

    [Fact]
    public void The_arrow_leans_while_it_moves_sideways_and_stands_up_when_it_stops()
    {
        var moves = Every33Ms(1, t => (100 + 600 * t, 250)).ToList();
        moves.Add(new CursorSample(1033, 700, 250)); // and then it rests
        var motion = new CursorMotion(Track(moves), Exact with { Sway = 1 });
        var upright = new CursorMotion(Track(moves), Exact);

        Assert.True(motion.PoseAt(0.5)!.Value.Angle > 5, "moving right turns it clockwise");
        Assert.InRange(motion.PoseAt(0.5)!.Value.Angle, 0, CursorMotion.MaxAngle);
        Assert.InRange(motion.PoseAt(2)!.Value.Angle, -0.5, 0.5);
        Assert.Equal(0, upright.PoseAt(0.5)!.Value.Angle);
    }

    [Fact]
    public void Moving_left_leans_the_other_way()
    {
        var motion = new CursorMotion(Track(Every33Ms(1, t => (800 - 600 * t, 250))), Exact with { Sway = 1 });

        Assert.True(motion.PoseAt(0.5)!.Value.Angle < -5);
    }

    [Fact]
    public void A_click_makes_the_pointer_shrink_a_moment_when_it_bounces()
    {
        CursorTrack track = Track([new(0, 500, 250)], new CursorClick(1000, 1100, 500, 250, ClickButton.Left));
        var bouncing = new CursorMotion(track, Exact with { Bounce = true });
        var still = new CursorMotion(track, Exact);

        Assert.Equal(1, bouncing.PoseAt(0.9)!.Value.Scale);
        Assert.Equal(1 - CursorMotion.BounceDepth, bouncing.PoseAt(1 + CursorMotion.BounceSeconds / 2)!.Value.Scale, 3);
        Assert.Equal(1, bouncing.PoseAt(1 + CursorMotion.BounceSeconds + 0.01)!.Value.Scale);
        Assert.Equal(1, still.PoseAt(1 + CursorMotion.BounceSeconds / 2)!.Value.Scale);
    }

    [Fact]
    public void A_click_shows_its_effect_for_a_moment_where_the_pointer_was()
    {
        CursorTrack track = Track([new(0, 500, 250)], new CursorClick(1000, 1000, 500, 250, ClickButton.Left));
        var motion = new CursorMotion(track, Exact with { Click = ClickEffect.Ripple });

        Assert.Empty(motion.ClicksAt(0.99));
        ClickMark mark = Assert.Single(motion.ClicksAt(1.2));
        Assert.Equal(0.5, mark.X, 3);
        Assert.Equal(0.5, mark.Y, 3);
        Assert.InRange(mark.Progress, 0.4, 0.5);
        Assert.Empty(motion.ClicksAt(1 + CursorMotion.ClickSeconds + 0.01));
        Assert.Empty(new CursorMotion(track, Exact with { Click = ClickEffect.None }).ClicksAt(1.2));
    }

    [Fact]
    public void Nothing_is_drawn_when_the_pointer_is_off_the_picture_or_switched_off()
    {
        CursorTrack track = Track([new(0, 500, 250), new(1000, 1200, 250)], new CursorClick(0, 0, 500, 250, ClickButton.Left));

        Assert.Null(new CursorMotion(track, Exact).PoseAt(1.5)); // past the right edge
        Assert.Null(new CursorMotion(track, Exact with { Visible = false }).PoseAt(0));
        Assert.Empty(new CursorMotion(track, Exact with { Visible = false }).ClicksAt(0.1));
        Assert.Null(new CursorMotion(Track([]), Exact).PoseAt(0));
    }

    [Fact]
    public void After_the_last_move_the_pointer_stays_where_it_ended()
    {
        var motion = new CursorMotion(Track([new(0, 100, 100), new(400, 300, 100)]), Exact);

        Assert.Equal(0.3, motion.PoseAt(60)!.Value.X, 3);
    }

    [Fact]
    public void A_style_from_a_hand_edited_file_is_brought_back_inside_what_is_offered()
    {
        CursorStyle style = new CursorStyle(true, (CursorShape)42, 9, double.NaN, -1, (ClickEffect)7, true).Tidied();

        Assert.Equal(CursorStyle.Default.Shape, style.Shape);
        Assert.Equal(CursorStyle.MaxSize, style.Size);
        Assert.Equal(CursorStyle.Default.Smoothing, style.Smoothing);
        Assert.Equal(0, style.Sway);
        Assert.Equal(CursorStyle.Default.Click, style.Click);
    }

    [Fact]
    public void A_track_saved_before_the_editor_drew_its_own_pointer_says_the_pointer_is_in_the_picture()
    {
        string folder = Path.Combine(Path.GetTempPath(), "zarpazo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string video = Path.Combine(folder, "old.mp4");
            File.WriteAllText(CursorTrack.PathFor(video), """{"Width":200,"Height":100,"Moves":[{"TimeMs":0,"X":1,"Y":2}],"Clicks":[]}""");
            new CursorTrack(200, 100, [], [], PointerInPicture: false, PointerScale: 1.5).Save(Path.Combine(folder, "new.mp4"));

            Assert.True(CursorTrack.Load(video)!.PointerInPicture);
            Assert.Equal(1, CursorTrack.Load(video)!.PointerScale);
            CursorTrack fresh = CursorTrack.Load(Path.Combine(folder, "new.mp4"))!;
            Assert.False(fresh.PointerInPicture);
            Assert.Equal(1.5, fresh.PointerScale);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
