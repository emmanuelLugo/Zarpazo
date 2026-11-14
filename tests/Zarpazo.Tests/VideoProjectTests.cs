using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zarpazo.History;
using Zarpazo.Recording;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

/// <summary>
/// An edit that is kept with its recording in the history, so "Editar" there opens the video as it was left: the file format,
/// loading it into the timeline, the history's files, what the delivery keeps, and the editor opening from it.
/// </summary>
[Collection("Recording")]
public sealed class VideoProjectTests : IClassFixture<SampleVideos>, IDisposable
{
    private readonly SampleVideos _samples;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));
    private readonly CaptureHistory _history;
    private readonly SettingsStore _settings;

    public VideoProjectTests(SampleVideos samples)
    {
        _samples = samples;
        Directory.CreateDirectory(_folder);
        _history = new CaptureHistory(Path.Combine(_folder, "history"));
        _settings = new SettingsStore(Path.Combine(_folder, "settings.json"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static BitmapSource Picture() =>
        BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgr32, null, new byte[8 * 6 * 4], 8 * 4);

    private string Media(string name, string content = "video")
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static readonly VideoLook TutorialLook = VideoLook.Default with { Zoom = new ZoomStyle(2, ZoomMotion.Focused, false) };

    private static VideoProject Project(double duration = 10)
    {
        var timeline = new EditTimeline(duration);
        timeline.Load(
            [new VideoSegment(0, 4), new VideoSegment(5, 9, Muted: true, Speed: 2)],
            [new ZoomRegion(1, 2.5, 2, 0.3, 0.6)],
            new VideoCrop(0.1, 0.05, 0.1, 0.2),
            [new VideoOverlay(OverlayKind.Text, 6, 8, 0.2, 0.3, Text: "Hola", Ink: Color.FromRgb(1, 2, 3), Size: 0.07),
             new VideoOverlay(OverlayKind.Blur, 0.5, 3, 0.1, 0.1, 0.4, 0.3)],
            TutorialLook);
        return VideoProject.Capture(timeline, TutorialLook);
    }

    // ---------- the file

    [Fact]
    public void A_project_is_read_back_exactly()
    {
        VideoProject project = Project();

        VideoProject? back = VideoProject.FromJson(project.ToJson());

        Assert.NotNull(back);
        Assert.Equal(project.ToSegments(), back.ToSegments());
        Assert.Equal(project.ToZooms(), back.ToZooms());
        Assert.Equal(project.ToOverlays(), back.ToOverlays()); // the text's colour, size and words too
        Assert.Equal(project.ToCrop(), back.ToCrop());
        Assert.Equal(TutorialLook, back.Look);
        Assert.Equal(10, back.SourceDuration);
    }

    [Fact]
    public void A_damaged_or_newer_project_is_left_out_instead_of_breaking_the_editor()
    {
        Assert.Null(VideoProject.FromJson("this is not json"));
        Assert.Null(VideoProject.FromJson("""{ "Version": 99, "Segments": [] }"""));
        Assert.Null(VideoProject.FromJson("""{ "Version": 1, "Segments": "no" }"""));
        Assert.Null(VideoProject.Load(Path.Combine(_folder, "nothing.json")));
    }

    // ---------- the timeline

    [Fact]
    public void Opening_an_edit_makes_it_the_start_with_nothing_to_undo_and_nothing_to_discard()
    {
        var timeline = new EditTimeline(10);
        VideoProject project = Project();

        timeline.Load(project.ToSegments(), project.ToZooms(), project.ToCrop(), project.ToOverlays(), TutorialLook);

        Assert.Equal(project.ToSegments(), timeline.Segments);
        Assert.Equal(project.ToZooms(), timeline.Zooms);
        Assert.Equal(2, timeline.Overlays.Count);
        Assert.False(timeline.HasChanges, "it is where the edit starts");
        Assert.False(timeline.CanUndo);
        Assert.Equal(TutorialLook, timeline.Look);

        timeline.SetCrop(VideoCrop.None);
        Assert.True(timeline.HasChanges);
        timeline.Undo();
        Assert.False(timeline.HasChanges); // back to what was opened, not to a plain recording
    }

    [Fact]
    public void Odd_values_in_a_file_are_tidied_and_nothing_usable_left_starts_as_the_whole_recording()
    {
        var timeline = new EditTimeline(10);

        timeline.Load(
            [new VideoSegment(8, 20), new VideoSegment(5, 5.05), new VideoSegment(double.NaN, 3), new VideoSegment(1, 2, Speed: 7)],
            [new ZoomRegion(1, 3, 2, 0.5, 0.5), new ZoomRegion(2, 5, 9, 2, -1)],
            new VideoCrop(0.9, 0, 0.9, 0),
            [new VideoOverlay(OverlayKind.Text, 4, 3, 0.5, 0.5, Text: "al revés"), new VideoOverlay(OverlayKind.Text, 1, 3, 0.5, 0.5, Text: "bien")],
            VideoLook.Default);

        Assert.Equal([new VideoSegment(8, 10), new VideoSegment(1, 2)], timeline.Segments); // clamped; the short and the NaN one gone; speed 7 is 1
        Assert.Equal(2, timeline.Zooms.Count);
        Assert.True(timeline.Zooms[1].Start >= timeline.Zooms[0].End); // never overlapping
        Assert.InRange(timeline.Zooms[1].Scale, ZoomRegion.MinScale, ZoomRegion.MaxScale);
        Assert.True(timeline.Crop.Width >= VideoCrop.MinKept - 1e-9);
        Assert.Equal("bien", Assert.Single(timeline.Overlays).Text);

        timeline.Load([new VideoSegment(5, 5.01)], [], VideoCrop.None, [], VideoLook.Default);
        Assert.Equal([new VideoSegment(0, 10)], timeline.Segments);
        Assert.False(timeline.HasChanges);
    }

    // ---------- the history

    [Fact]
    public void A_recording_kept_with_its_source_can_be_edited_again_and_goes_with_all_its_files()
    {
        string original = Media("rec.original.mp4", "clean recording");
        new CursorTrack(8, 6, [], []).Save(original);
        string finished = Media("rec.mp4", "finished");

        string path = _history.AddRecording(finished, Picture(), original: original, project: Project());

        HistoryEntry entry = Assert.Single(_history.List());
        Assert.True(entry.CanEdit);
        Assert.True(entry.IsRecording);
        Assert.Equal("finished", File.ReadAllText(entry.MediaPath!)); // what is copied and saved is the finished video
        Assert.True(CaptureHistory.IsRecordingEntry(path));
        (VideoProject project, string source) = _history.LoadProject(path)!.Value;
        Assert.Equal("clean recording", File.ReadAllText(source));
        Assert.True(File.Exists(CursorTrack.PathFor(source)));
        Assert.Equal(Project().ToSegments(), project.ToSegments());

        _history.Delete(path);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_folder, "history"))); // not a file left behind
    }

    [Fact]
    public void A_recording_closed_before_saving_is_kept_once_and_that_one_is_the_video_shown()
    {
        string original = Media("rec.original.mp4", "clean recording");

        string path = _history.AddRecording(original, Picture(), original: original, project: Project());

        HistoryEntry entry = Assert.Single(_history.List());
        Assert.True(entry.CanEdit);
        Assert.Equal(CaptureHistory.SourcePath(path), entry.MediaPath);
        Assert.Equal("Video", entry.Kind);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_folder, "history"), "*.mp4")); // not twice
    }

    [Fact]
    public void A_recording_kept_before_this_existed_is_a_video_to_copy_but_has_nothing_to_edit()
    {
        string path = _history.AddRecording(Media("old.mp4"), Picture());

        Assert.False(Assert.Single(_history.List()).CanEdit);
        Assert.Null(_history.LoadProject(path));
        Assert.False(CaptureHistory.IsRecordingEntry(path));
    }

    [Fact]
    public void A_damaged_project_file_is_no_edit_but_the_video_stays()
    {
        string original = Media("rec.original.mp4");
        string path = _history.AddRecording(Media("rec.mp4"), Picture(), original: original, project: Project());
        File.WriteAllText(Path.ChangeExtension(path, ".project.json"), "{ broken");

        Assert.Null(_history.LoadProject(path));
        Assert.True(Assert.Single(_history.List()).IsRecording);
    }

    // ---------- the delivery

    private VideoDelivery Delivery(string original, string? entry = null) =>
        new(original, Path.Combine(_folder, "ready.mp4"), _settings, _history, Picture(), historyPath: entry);

    private static Task Make(string target)
    {
        File.WriteAllText(target, "made");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Handing_out_a_video_keeps_its_recording_and_edit_and_a_second_one_takes_the_entry_over()
    {
        string original = Media("rec.original.mp4", "clean recording");
        VideoDelivery delivery = Delivery(original);

        await delivery.PrepareAsync("one", Make, Project());
        await delivery.HistoryWork; // the copy into the history goes on in the background
        string first = Assert.Single(_history.List()).Path;
        Assert.True(_history.LoadProject(first) != null);
        _history.SetKept(first, true); // the person marked it to keep

        await delivery.PrepareAsync("two", Make, Project());
        await delivery.HistoryWork;

        HistoryEntry entry = Assert.Single(_history.List()); // replaced, not added
        Assert.NotEqual(first, entry.Path);
        Assert.True(entry.Kept, "what was marked to keep stays so");
        Assert.True(entry.CanEdit);
        Assert.False(File.Exists(first));
    }

    [Fact]
    public async Task Closing_without_handing_anything_out_keeps_the_recording_with_the_edit()
    {
        VideoDelivery delivery = Delivery(Media("rec.original.mp4", "clean recording"));

        await delivery.FinishAsync(Project()); // in the background: closing does not wait for the copy

        HistoryEntry entry = Assert.Single(_history.List());
        Assert.True(entry.CanEdit);
        Assert.False(VideoDelivery.HasPendingWrites, "nothing is left to write");
        Assert.Equal(Project().ToZooms(), _history.LoadProject(entry.Path)!.Value.Project.ToZooms());
    }

    [Fact]
    public async Task Carrying_on_and_closing_without_handing_out_keeps_the_entrys_video_and_only_updates_its_edit()
    {
        string path = _history.AddRecording(Media("done.mp4", "finished"), Picture(), original: Media("done.original.mp4", "clean"), project: Project());
        VideoDelivery delivery = Delivery(Media("work.original.mp4", "work copy"), path);
        var timeline = new EditTimeline(10);
        timeline.Load([new VideoSegment(0, 10)], [new ZoomRegion(1, 3, 3, 0.5, 0.5)], VideoCrop.None, [], VideoLook.Default);

        await delivery.FinishAsync(VideoProject.Capture(timeline, VideoLook.Default));

        HistoryEntry entry = Assert.Single(_history.List());
        Assert.Equal(path, entry.Path); // the same entry, not a new one
        Assert.Equal("finished", File.ReadAllText(entry.MediaPath!)); // the video was not replaced by the raw recording
        Assert.Equal(3, _history.LoadProject(path)!.Value.Project.ToZooms()[0].Scale); // the edit is the latest
    }

    [Fact]
    public async Task Carrying_on_and_handing_out_replaces_the_entry_and_still_has_the_recording_to_edit_again()
    {
        string path = _history.AddRecording(Media("done.mp4", "finished"), Picture(), original: Media("done.original.mp4", "clean"), project: Project());
        VideoDelivery delivery = Delivery(Media("work.original.mp4", "work copy"), path);

        await delivery.PrepareAsync("new", Make, Project());
        await delivery.FinishAsync(Project());

        HistoryEntry entry = Assert.Single(_history.List());
        Assert.NotEqual(path, entry.Path);
        Assert.Equal("made", File.ReadAllText(entry.MediaPath!));
        Assert.Equal("work copy", File.ReadAllText(_history.LoadProject(entry.Path)!.Value.Source));
    }

    // ---------- opening from the history

    [Fact]
    public async Task The_workspace_copies_the_recording_and_its_pointer_and_hands_over_the_edit()
    {
        string original = Media("rec.original.mp4", "clean recording");
        new CursorTrack(8, 6, [], []).Save(original);
        string path = _history.AddRecording(Media("rec.mp4"), Picture(), original: original, project: Project());
        string folder = Path.Combine(_folder, "work");

        ProjectWorkspace.Prepared prepared = (await ProjectWorkspace.PrepareAsync(_history, _settings, path, folder))!;

        Assert.Equal("clean recording", File.ReadAllText(prepared.Work));
        Assert.StartsWith(folder, prepared.Work);
        Assert.True(File.Exists(CursorTrack.PathFor(prepared.Work)));
        Assert.Equal(prepared.Work, prepared.Delivery.Original);
        Assert.Equal(Project().ToZooms(), prepared.Project.ToZooms());
        Assert.NotEqual(prepared.Work, prepared.Delivery.ReadyPath);
        Assert.True(File.Exists(CaptureHistory.SourcePath(path)), "the history's own files are left alone");

        string old = _history.AddRecording(Media("old.mp4"), Picture());
        Assert.Null(await ProjectWorkspace.PrepareAsync(_history, _settings, old, folder)); // nothing to carry on from
    }

    private static bool Pump(Func<bool> until, int milliseconds = 15000)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            if (until())
            {
                return true;
            }

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
        return until();
    }

    [Fact]
    public async Task The_editor_opens_a_video_of_the_history_with_its_cuts_zooms_layers_crop_and_style_as_they_were_left()
    {
        // A real recording as the source: its project is smaller than the one above (the sample is about 3 seconds).
        var edit = new EditTimeline(2.9);
        edit.Load(
            [new VideoSegment(0, 1), new VideoSegment(1.5, 2.5)],
            [new ZoomRegion(0.2, 1, 2, 0.5, 0.5)],
            new VideoCrop(0.1, 0.1, 0.1, 0.1),
            [new VideoOverlay(OverlayKind.Text, 1.6, 2.4, 0.3, 0.3, Text: "Aquí")],
            TutorialLook);
        string path = _history.AddRecording(Media("rec.mp4"), Picture(), original: _samples.Silent, project: VideoProject.Capture(edit, TutorialLook));
        ZoomStyle remembered = _settings.Current.VideoZoom;
        ProjectWorkspace.Prepared prepared = (await ProjectWorkspace.PrepareAsync(_history, _settings, path, Path.Combine(_folder, "work")))!;

        Task? written = null;
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(prepared.Work, _settings, prepared.Delivery, project: prepared.Project);
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), $"the video should open: {window.KeptText.Text}");
                EditTimeline timeline = window.Timeline!;
                Assert.Equal(edit.Segments, timeline.Segments);
                Assert.Equal(edit.Zooms, timeline.Zooms);
                Assert.Equal(edit.Overlays, timeline.Overlays);
                Assert.Equal(edit.Crop, timeline.Crop);
                Assert.Equal(TutorialLook.Zoom, window.Look.Zoom);
                Assert.False(window.ZoomConnectBox.IsChecked); // the panel shows the style the video was left with
                Assert.Equal(remembered, _settings.Current.VideoZoom); // the one remembered for new videos is not touched
                Assert.False(timeline.HasChanges, "nothing was changed: closing asks nothing");
                Assert.False(timeline.CanUndo);
            }
            finally
            {
                window.Close(); // would stop to ask about changes if there were any
                written = window.HistoryWork;
            }
        });
        Assert.NotNull(written); // closing does not wait for the history: it goes on in the background
        await written;

        HistoryEntry entry = Assert.Single(_history.List());
        Assert.Equal(path, entry.Path); // closed without handing anything out: the entry is as it was
        Assert.Equal(edit.Segments, _history.LoadProject(path)!.Value.Project.ToSegments());
    }
}
