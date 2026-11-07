using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class RecordingClockTests
{
    [Fact]
    public void It_counts_time_while_running()
    {
        var clock = new RecordingClock();
        clock.Start();

        Thread.Sleep(200);

        Assert.InRange(clock.ElapsedMilliseconds, 150, 400);
    }

    [Fact]
    public void Time_stands_still_while_paused_and_carries_on_after()
    {
        var clock = new RecordingClock();
        clock.Start();
        Thread.Sleep(150);

        clock.Pause();
        long atPause = clock.ElapsedMilliseconds;
        Thread.Sleep(400); // the pause must not count
        long afterPause = clock.ElapsedMilliseconds;
        clock.Resume();
        Thread.Sleep(150);
        long end = clock.ElapsedMilliseconds;

        Assert.True(clock.IsPaused == false);
        Assert.Equal(atPause, afterPause);
        Assert.InRange(end - atPause, 100, 350); // only the 150 ms after resuming, not the 400 ms of the pause
    }

    [Fact]
    public void Pausing_twice_or_resuming_when_not_paused_changes_nothing()
    {
        var clock = new RecordingClock();
        clock.Start();
        clock.Resume(); // not paused
        Thread.Sleep(100);
        clock.Pause();
        long first = clock.ElapsedMilliseconds;
        Thread.Sleep(100);
        clock.Pause(); // already paused: must not restart the pause from later

        Assert.Equal(first, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void Stopping_while_paused_leaves_the_time_at_the_pause()
    {
        var clock = new RecordingClock();
        clock.Start();
        Thread.Sleep(100);
        clock.Pause();
        long atPause = clock.ElapsedMilliseconds;
        Thread.Sleep(100);

        clock.Stop();

        Assert.Equal(atPause, clock.ElapsedMilliseconds);
    }
}
