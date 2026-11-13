using System.IO;
using Zarpazo.Recording;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

/// <summary>Presets: a look (frame, pointer, zoom style) saved with a name, the zoom's motion, undoing a look and the settings file.</summary>
public sealed class VideoPresetTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static readonly VideoLook Tutorial = new(
        VideoFrameStyle.Default with { Enabled = true, Padding = 0.1 },
        CursorStyle.Default with { Size = 2.5 },
        new ZoomStyle(2, ZoomMotion.Focused));

    [Theory]
    [InlineData("  Mi   estilo ", "Mi estilo")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Names_are_trimmed_and_an_empty_one_is_no_name(string? name, string? expected)
    {
        Assert.Equal(expected, VideoPreset.TidyName(name));
        Assert.Equal(VideoPreset.MaxNameLength, VideoPreset.TidyName(new string('a', 100))!.Length);
    }

    [Fact]
    public void Saving_under_a_name_already_there_replaces_that_preset_and_names_are_unique()
    {
        IReadOnlyList<VideoPreset> list = VideoPresetList.Save([], "Tutorial", VideoLook.Default)!;
        list = VideoPresetList.Save(list, "Demo", VideoLook.Default)!;

        list = VideoPresetList.Save(list, "tutorial", Tutorial)!; // the same name, whatever the case

        Assert.Equal(["Tutorial", "Demo"], list.Select(preset => preset.Name));
        Assert.Equal(Tutorial, list[0].Look);
        Assert.Null(VideoPresetList.Save(list, "  ", Tutorial));
    }

    [Fact]
    public void Renaming_refuses_a_name_another_preset_has_and_deleting_takes_it_out()
    {
        IReadOnlyList<VideoPreset> list = [new("Tutorial", Tutorial), new("Demo", VideoLook.Default)];

        Assert.Null(VideoPresetList.Rename(list, "Demo", "TUTORIAL"));
        Assert.Null(VideoPresetList.Rename(list, "Nadie", "Otro"));
        IReadOnlyList<VideoPreset> renamed = VideoPresetList.Rename(list, "demo", "Presentación")!;
        Assert.Equal(["Tutorial", "Presentación"], renamed.Select(preset => preset.Name));
        Assert.NotNull(VideoPresetList.Rename(list, "Demo", "demo")); // its own name in other letters

        Assert.Equal(["Presentación"], VideoPresetList.Delete(renamed, "Tutorial")!.Select(preset => preset.Name));
        Assert.Null(VideoPresetList.Delete(renamed, "Nadie"));
    }

    [Fact]
    public void Focused_zooms_in_and_out_quicker_than_smooth()
    {
        VideoSegment[] whole = [new VideoSegment(0, 20)];
        ZoomRegion[] zoom = [new ZoomRegion(5, 10, 2, 0.5, 0.5)];
        var smooth = new ZoomCamera(whole, zoom, ZoomMotion.Smooth);
        var focused = new ZoomCamera(whole, zoom, ZoomMotion.Focused);

        Assert.Equal(2, focused.ViewAt(5 + ZoomTiming.Focused.InSeconds + 0.01).Scale, 2); // already all the way in
        Assert.True(smooth.ViewAt(5 + ZoomTiming.Focused.InSeconds + 0.01).Scale < 1.95);
        Assert.Equal(2, focused.ViewAt(10 - ZoomTiming.Focused.OutSeconds - 0.01).Scale, 2); // not yet on its way out
        Assert.True(smooth.ViewAt(10 - ZoomTiming.Focused.OutSeconds - 0.01).Scale < 1.95);
        Assert.Equal(new ZoomCamera(whole, zoom).ViewAt(5.7), smooth.ViewAt(5.7)); // smooth is how it always was
    }

    [Fact]
    public void A_new_look_is_one_step_to_undo_gives_its_depth_to_every_zoom_and_is_no_change_to_discard()
    {
        var timeline = new EditTimeline(10);
        timeline.StartWithZooms([new ZoomRegion(1, 2, 1.5, 0.5, 0.5), new ZoomRegion(5, 6, 1.8, 0.2, 0.2)]);
        timeline.StartWithLook(VideoLook.Default);
        Assert.False(timeline.CanUndo);

        Assert.True(timeline.SetLook(Tutorial));

        Assert.Equal(Tutorial, timeline.Look);
        Assert.All(timeline.Zooms, zoom => Assert.Equal(2, zoom.Scale));
        timeline.Undo();
        Assert.Equal(VideoLook.Default, timeline.Look);
        Assert.Equal([1.5, 1.8], timeline.Zooms.Select(zoom => zoom.Scale));
        Assert.False(timeline.CanUndo);

        timeline.SetLook(VideoLook.Default with { Frame = Tutorial.Frame }); // the zoom's depth stays: the zooms too
        Assert.Equal([1.5, 1.8], timeline.Zooms.Select(zoom => zoom.Scale));
        Assert.False(timeline.HasChanges, "a style is remembered for the next video, not an edit to discard");
        Assert.False(timeline.SetLook(timeline.Look));
    }

    [Fact]
    public void Presets_and_the_zoom_style_are_read_back_by_the_next_run()
    {
        var first = new SettingsStore(FilePath);
        first.Current.VideoZoom = new ZoomStyle(2.5, ZoomMotion.Focused);
        first.Current.Presets = VideoPresetList.Save([], "Tutorial", Tutorial)!;
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;

        Assert.Equal(new ZoomStyle(2.5, ZoomMotion.Focused), second.VideoZoom);
        Assert.Equal(Tutorial, Assert.Single(second.Presets).Look);
        Assert.Equal("Tutorial", second.Presets[0].Name);
    }

    [Fact]
    public void A_hand_edited_file_with_odd_presets_is_tidied_instead_of_lost()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """
        {
          "SaveFormat": "jpg",
          "VideoZoomDepth": 40, "VideoZoomMotion": "Rapidísimo",
          "VideoPresets": [
            null,
            { "Name": "  Bueno  ", "Look": { "Frame": { "Enabled": true, "Background": "nada", "Padding": 9, "Radius": -1, "Shadow": 3 },
                                             "Cursor": { "Visible": true, "Shape": 42, "Size": 99, "Smoothing": -2, "Sway": 0.3, "Click": 7, "Bounce": false },
                                             "Zoom": { "Depth": 0, "Motion": 9 } } },
            { "Name": "bueno", "Look": null },
            { "Name": "", "Look": null },
            { "Name": "Sin estilo" }
          ]
        }
        """);

        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal("jpg", settings.SaveFormat); // the rest of the file is kept
        Assert.Equal(new ZoomStyle(ZoomRegion.MaxScale, ZoomMotion.Smooth), settings.VideoZoom);
        Assert.Equal(["Bueno", "Sin estilo"], settings.Presets.Select(preset => preset.Name));
        VideoLook look = settings.Presets[0].Look;
        Assert.Equal(VideoFrameStyle.MaxPadding, look.Frame.Padding);
        Assert.Equal(0, look.Frame.Radius);
        Assert.Equal(1, look.Frame.Shadow);
        Assert.Equal(CursorStyle.Default.Shape, look.Cursor.Shape);
        Assert.Equal(CursorStyle.MaxSize, look.Cursor.Size);
        Assert.Equal(CursorStyle.Default.Click, look.Cursor.Click);
        Assert.Equal(new ZoomStyle(ZoomRegion.MinScale, ZoomMotion.Smooth), look.Zoom);
        Assert.Equal(VideoLook.Default, settings.Presets[1].Look);
    }
}
