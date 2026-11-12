using System.Drawing;
using System.IO;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class CursorRecorderTests : IDisposable
{
    private const int Left = 0x01;
    private const int Right = 0x02;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    // A mouse the test moves by hand; it also counts how often Windows would have been asked.
    private sealed class FakeMouse : IInputState
    {
        private readonly HashSet<int> _down = [];
        private readonly HashSet<int> _quick = [];

        public Point CursorPosition { get; set; }

        public int Reads { get; private set; }

        public void Press(int key) => _down.Add(key);

        public void Release(int key) => _down.Remove(key);

        public void QuickClick(int key) => _quick.Add(key);

        public bool IsDown(int virtualKey)
        {
            Reads++;
            return _down.Contains(virtualKey);
        }

        // Like Windows: reading the bit clears it.
        public bool WasPressedSinceLastCheck(int virtualKey) => _quick.Remove(virtualKey);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void Positions_are_kept_in_pixels_of_the_video_and_only_when_the_pointer_moves()
    {
        // A 400x200 area at (100, 50) recorded into a video half that size.
        var mouse = new FakeMouse { CursorPosition = new Point(300, 150) };
        var recorder = new CursorRecorder(mouse, new Rectangle(100, 50, 400, 200), 200, 100);

        recorder.Update(0);
        recorder.Update(33); // still in the same place
        mouse.CursorPosition = new Point(500, 250);
        recorder.Update(66);

        CursorTrack track = recorder.ToTrack(100);
        Assert.Equal((200, 100), (track.Width, track.Height));
        Assert.Equal([new CursorSample(0, 100, 50), new CursorSample(66, 200, 100)], track.Moves);
    }

    [Fact]
    public void A_click_keeps_where_it_went_down_and_how_long_it_was_held()
    {
        var mouse = new FakeMouse { CursorPosition = new Point(10, 20) };
        var recorder = new CursorRecorder(mouse, new Rectangle(0, 0, 100, 100), 100, 100);

        mouse.Press(Left);
        recorder.Update(100);
        mouse.CursorPosition = new Point(60, 70); // a drag: the click stays where it began
        recorder.Update(133);
        mouse.Release(Left);
        recorder.Update(166);

        Assert.Equal([new CursorClick(100, 166, 10, 20, ClickButton.Left)], recorder.ToTrack(200).Clicks);
    }

    [Fact]
    public void A_click_over_before_the_next_picture_still_counts()
    {
        var mouse = new FakeMouse { CursorPosition = new Point(40, 30) };
        var recorder = new CursorRecorder(mouse, new Rectangle(0, 0, 100, 100), 100, 100);

        mouse.QuickClick(Right);
        recorder.Update(500);

        Assert.Equal([new CursorClick(500, 500, 40, 30, ClickButton.Right)], recorder.ToTrack(600).Clicks);
    }

    [Fact]
    public void A_button_still_held_when_the_recording_ends_is_released_at_the_end()
    {
        var mouse = new FakeMouse();
        var recorder = new CursorRecorder(mouse, new Rectangle(0, 0, 100, 100), 100, 100);

        mouse.Press(Left);
        recorder.Update(1000);

        Assert.Equal(1800, Assert.Single(recorder.ToTrack(1800).Clicks).EndMs);
    }

    [Fact]
    public void The_shared_input_answers_everyone_the_same_until_the_next_picture()
    {
        var mouse = new FakeMouse();
        var shared = new FrameInputState(mouse);

        mouse.QuickClick(Left);
        Assert.True(shared.WasPressedSinceLastCheck(Left));
        Assert.True(shared.WasPressedSinceLastCheck(Left)); // the second reader sees the same quick click
        shared.IsDown(Left);
        shared.IsDown(Left);
        Assert.Equal(1, mouse.Reads);

        shared.Refresh();
        Assert.False(shared.WasPressedSinceLastCheck(Left));
    }

    [Fact]
    public void The_track_is_saved_next_to_the_video_and_read_back()
    {
        Directory.CreateDirectory(_folder);
        string video = Path.Combine(_folder, "Grabación.mp4");
        var track = new CursorTrack(320, 180, [new CursorSample(0, 1, 2)], [new CursorClick(10, 90, 3, 4, ClickButton.Middle)]);

        track.Save(video);

        Assert.True(File.Exists(Path.Combine(_folder, "Grabación.cursor.json")));
        CursorTrack? read = CursorTrack.Load(video);
        Assert.NotNull(read);
        Assert.Equal((320, 180), (read.Width, read.Height));
        Assert.Equal(track.Moves, read.Moves);
        Assert.Equal(track.Clicks, read.Clicks);
    }

    [Fact]
    public void A_video_without_a_track_or_with_a_broken_one_has_none()
    {
        Directory.CreateDirectory(_folder);
        string video = Path.Combine(_folder, "old.mp4");
        Assert.Null(CursorTrack.Load(video));

        File.WriteAllText(CursorTrack.PathFor(video), "{ not json");
        Assert.Null(CursorTrack.Load(video));
    }
}
