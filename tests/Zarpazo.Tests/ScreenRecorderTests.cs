using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Records a real corner of the screen: nothing is shown, but the desktop has to be available.</summary>
[Collection("Recording")]
public sealed class ScreenRecorderTests
{
    private static readonly Rectangle Corner = new(0, 0, 160, 120);

    [Fact]
    public void A_recording_has_a_first_full_frame_and_frames_in_time_order()
    {
        var recorder = new ScreenRecorder(Corner, TimeSpan.FromSeconds(10), maxBytes: 100_000_000);
        recorder.Start();
        Thread.Sleep(700);

        (IReadOnlyList<RecordedFrame> frames, int durationMs) = recorder.Stop();

        Assert.NotEmpty(frames);
        Assert.Equal((0, 0, 160, 120), (frames[0].X, frames[0].Y, frames[0].Width, frames[0].Height));
        Assert.Equal(160 * 120 * 4, frames[0].Pixels.Length);
        Assert.Equal(frames.Select(f => f.TimeMs).OrderBy(t => t), frames.Select(f => f.TimeMs));
        Assert.InRange(durationMs, 600, 1500);
    }

    [Fact]
    public void What_it_records_encodes_to_a_gif_with_one_image_per_frame()
    {
        var recorder = new ScreenRecorder(Corner, TimeSpan.FromSeconds(10), maxBytes: 100_000_000);
        recorder.Start();
        Thread.Sleep(500);
        (IReadOnlyList<RecordedFrame> frames, int durationMs) = recorder.Stop();

        using var gif = new MemoryStream();
        GifEncoder.Encode(frames, recorder.Width, recorder.Height, durationMs, gif);
        gif.Position = 0;

        Sta.Run(() =>
        {
            BitmapDecoder decoder = BitmapDecoder.Create(gif, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(frames.Count, decoder.Frames.Count);
            Assert.Equal((160, 120), (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight));
        });
    }

    [Fact]
    public void It_stops_by_itself_at_the_time_limit_and_says_so()
    {
        var recorder = new ScreenRecorder(Corner, TimeSpan.FromMilliseconds(300), maxBytes: 100_000_000);
        var reasons = new List<RecordingEnd>();
        using var raised = new ManualResetEventSlim();
        recorder.LimitReached += reason =>
        {
            reasons.Add(reason);
            raised.Set();
        };

        recorder.Start();

        Assert.True(raised.Wait(TimeSpan.FromSeconds(5)), "the limit was never reported");
        recorder.Stop();
        Assert.Equal([RecordingEnd.TimeLimit], reasons);
    }

    [Fact]
    public void It_stops_by_itself_when_the_frames_take_too_much_memory()
    {
        var recorder = new ScreenRecorder(Corner, TimeSpan.FromSeconds(10), maxBytes: 1000); // smaller than the first frame
        using var raised = new ManualResetEventSlim();
        RecordingEnd? reason = null;
        recorder.LimitReached += why =>
        {
            reason = why;
            raised.Set();
        };

        recorder.Start();

        Assert.True(raised.Wait(TimeSpan.FromSeconds(5)));
        recorder.Stop();
        Assert.Equal(RecordingEnd.MemoryLimit, reason);
    }

    [Fact]
    public void A_large_area_is_shrunk_to_the_size_limit_keeping_its_proportions()
    {
        var recorder = new ScreenRecorder(new Rectangle(0, 0, 2560, 1440), TimeSpan.FromSeconds(10), maxBytes: 100_000_000);

        Assert.Equal((1280, 720), (recorder.Width, recorder.Height));
    }

    [Fact]
    public void A_small_area_keeps_its_size()
    {
        var recorder = new ScreenRecorder(new Rectangle(0, 0, 640, 360), TimeSpan.FromSeconds(10), maxBytes: 100_000_000);

        Assert.Equal((640, 360), (recorder.Width, recorder.Height));
    }

    [Fact]
    public void The_time_spent_paused_is_not_part_of_the_recording()
    {
        var recorder = new ScreenRecorder(Corner, TimeSpan.FromSeconds(10), maxBytes: 100_000_000);
        recorder.Start();
        Thread.Sleep(500);

        recorder.Pause();
        Assert.True(recorder.IsPaused);
        Thread.Sleep(1000); // a long pause
        recorder.Resume();
        Thread.Sleep(500);
        (IReadOnlyList<RecordedFrame> frames, int durationMs) = recorder.Stop();

        Assert.InRange(durationMs, 800, 1400); // about a second of recording, not the two and a half that went by
        Assert.All(frames, frame => Assert.InRange(frame.TimeMs, 0, durationMs));
    }
}
