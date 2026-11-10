using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Editor;
using Zarpazo.History;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

public sealed class CaptureHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private static BitmapSource Image(int width, int height) =>
        BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[width * height * 4], width * 4);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
        try
        {
            Directory.Delete(Path.GetDirectoryName(_folder)!); // the shared parent, once no other test is using it
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Lists_nothing_before_the_first_capture()
    {
        Assert.Empty(new CaptureHistory(_folder).List());
    }

    [Fact]
    public void Lists_newest_first_even_when_captures_share_a_millisecond()
    {
        var history = new CaptureHistory(_folder);
        string[] added = Enumerable.Range(1, 5).Select(i => history.Add(Image(i, 1), limit: 100)).ToArray();

        Assert.Equal(added.Reverse(), history.List().Select(entry => entry.Path));
    }

    [Fact]
    public void Drops_the_oldest_captures_beyond_the_limit()
    {
        var history = new CaptureHistory(_folder);
        string[] added = Enumerable.Range(1, 5).Select(i => history.Add(Image(i, 1), limit: 3)).ToArray();

        Assert.Equal(added.Skip(2).Reverse(), history.List().Select(entry => entry.Path));
    }

    [Fact]
    public void Loads_the_image_back_and_does_not_keep_the_file_open()
    {
        var history = new CaptureHistory(_folder);
        string path = history.Add(Image(30, 20), limit: 100);

        BitmapSource loaded = CaptureHistory.Load(path);
        BitmapSource thumbnail = CaptureHistory.LoadThumbnail(path, 10);
        history.Delete(path);

        Assert.Equal((30, 20), (loaded.PixelWidth, loaded.PixelHeight));
        Assert.Equal(10, thumbnail.PixelWidth);
        Assert.Empty(history.List());
    }

    private static EditState StateOf(int width, int height) => new()
    {
        Left = -1920, Top = 40, Width = width, Height = height,
        Annotations = [new AnnotationData { Tool = "Line", Color = "#E53935", Thickness = 4, Points = [1, 2, 3, 4] }],
    };

    [Fact]
    public void An_annotated_capture_reopens_on_its_clean_image_with_its_annotations()
    {
        var history = new CaptureHistory(_folder);
        string path = history.Add(Image(30, 20), 100, StateOf(30, 20), clean: Image(31, 21));

        Assert.True(history.List().Single().CanEdit);
        (EditState state, BitmapSource baseImage) = history.LoadEditable(path)!.Value;

        Assert.Equal((-1920, 40, 30, 20), (state.Left, state.Top, state.Width, state.Height));
        Assert.Equal(new double[] { 1, 2, 3, 4 }, state.Annotations.Single().Points);
        Assert.Equal((31, 21), (baseImage.PixelWidth, baseImage.PixelHeight)); // the clean one, not the finished picture
    }

    [Fact]
    public void A_capture_without_annotations_uses_the_picture_itself_as_the_base()
    {
        var history = new CaptureHistory(_folder);
        string path = history.Add(Image(30, 20), 100, new EditState { Width = 30, Height = 20 });

        (_, BitmapSource baseImage) = history.LoadEditable(path)!.Value;

        Assert.Equal((30, 20), (baseImage.PixelWidth, baseImage.PixelHeight));
        Assert.Empty(Directory.GetFiles(_folder, "*.clean")); // no second copy of an image that has nothing drawn on it
    }

    [Fact]
    public void A_plain_capture_or_one_with_damaged_state_cannot_be_reopened_but_still_lists()
    {
        var history = new CaptureHistory(_folder);
        string plain = history.Add(Image(5, 5), 100);
        string damaged = history.Add(Image(5, 5), 100, StateOf(5, 5));
        File.WriteAllText(Path.ChangeExtension(damaged, ".json"), "{ not json");

        Assert.Null(history.LoadEditable(plain));
        Assert.Null(history.LoadEditable(damaged));
        Assert.Equal(2, history.List().Count);
        Assert.False(history.List().Single(entry => entry.Path == plain).CanEdit);
    }

    [Fact]
    public void Deleting_and_pruning_remove_the_files_that_belong_to_a_capture()
    {
        var history = new CaptureHistory(_folder);
        string first = history.Add(Image(5, 5), 100, StateOf(5, 5), clean: Image(5, 5));
        history.Delete(first);
        Assert.Empty(Directory.GetFiles(_folder));

        history.Add(Image(5, 5), 1, StateOf(5, 5), clean: Image(5, 5));
        history.Add(Image(6, 6), 1, StateOf(6, 6), clean: Image(6, 6));

        Assert.Equal(3, Directory.GetFiles(_folder).Length); // one capture: picture, state and clean image
    }

    [Fact]
    public void A_hand_edited_limit_below_one_falls_back_to_the_default()
    {
        var settings = new AppSettings { HistoryLimit = 0 };

        settings.Normalize();

        Assert.Equal(AppSettings.DefaultHistoryLimit, settings.HistoryLimit);
    }

    private string MediaFile(string extension, string content = "media")
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "source-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_recording_is_kept_as_a_copy_of_its_file_plus_a_picture_that_makes_it_show_in_the_list()
    {
        var history = new CaptureHistory(_folder);
        string gif = MediaFile(".gif", "pretend gif");

        string path = history.AddRecording(gif, Image(8, 6));

        HistoryEntry entry = Assert.Single(history.List());
        Assert.True(entry.IsRecording);
        Assert.Equal("GIF", entry.Kind);
        Assert.Equal(path, entry.Path);
        Assert.Equal("pretend gif", File.ReadAllText(entry.MediaPath!));
        Assert.True(File.Exists(gif), "the original file is left where it was: the clipboard may point at it");
        Assert.False(entry.CanEdit);
    }

    [Fact]
    public void A_video_is_told_apart_from_a_gif_by_its_extension()
    {
        var history = new CaptureHistory(_folder);

        history.AddRecording(MediaFile(".mp4"), Image(8, 6));

        Assert.Equal("Video", Assert.Single(history.List()).Kind);
    }

    [Fact]
    public void A_capture_is_not_a_recording()
    {
        var history = new CaptureHistory(_folder);

        history.Add(Image(8, 6), limit: 100);

        HistoryEntry entry = Assert.Single(history.List());
        Assert.False(entry.IsRecording);
        Assert.Null(entry.MediaPath);
    }

    [Fact]
    public void Deleting_a_recording_removes_its_file_and_its_picture()
    {
        var history = new CaptureHistory(_folder);
        string path = history.AddRecording(MediaFile(".mp4"), Image(8, 6));
        string media = history.List()[0].MediaPath!;

        history.Delete(path);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(media));
        Assert.Empty(history.List());
    }

    [Fact]
    public void Only_the_newest_recordings_are_kept_and_captures_are_left_alone()
    {
        var history = new CaptureHistory(_folder);
        history.Add(Image(8, 6), limit: 100);
        for (int i = 0; i < 4; i++)
        {
            history.AddRecording(MediaFile(".gif", $"recording {i}"), Image(8, 6), recordingLimit: 2);
            Thread.Sleep(5);
        }

        IReadOnlyList<HistoryEntry> entries = history.List();

        Assert.Equal(2, entries.Count(entry => entry.IsRecording));
        Assert.Single(entries, entry => !entry.IsRecording); // the capture survived
        // The two that remain are the last two made.
        Assert.Equal(["recording 3", "recording 2"], entries.Where(entry => entry.IsRecording).Select(entry => File.ReadAllText(entry.MediaPath!)));
    }

    [Fact]
    public void Text_is_unread_until_written_and_an_empty_text_counts_as_read()
    {
        var history = new CaptureHistory(_folder);
        string path = history.Add(Image(8, 6), limit: 100);
        Assert.Null(history.ReadText(path));

        history.WriteText(path, "");
        Assert.Equal("", history.ReadText(path));

        history.WriteText(path, "Factura 123");
        Assert.Equal("Factura 123", history.ReadText(path));
    }

    [Fact]
    public void Deleting_a_capture_removes_its_text_and_a_deleted_one_gets_none()
    {
        var history = new CaptureHistory(_folder);
        string path = history.Add(Image(8, 6), limit: 100);
        history.WriteText(path, "hola");

        history.Delete(path);
        history.WriteText(path, "late result"); // the reading finished after the delete

        Assert.False(File.Exists(Path.ChangeExtension(path, ".txt")));
    }

    [Theory]
    [InlineData("Factura número 123", "FACTURA", true)]
    [InlineData("Factura número 123", "numero", true)]
    [InlineData("Factura número 123", "  123 ", true)]
    [InlineData("Factura número 123", "recibo", false)]
    [InlineData("", "recibo", false)]
    [InlineData(null, "recibo", false)]
    [InlineData(null, "", true)]
    public void Search_ignores_case_and_accents(string? text, string query, bool expected)
    {
        Assert.Equal(expected, CaptureHistory.Matches(text, query));
    }

    private string Aged(string stamp)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, $"Zarpazo_{stamp}.png");
        File.WriteAllText(path, "x");
        File.WriteAllText(Path.ChangeExtension(path, ".json"), "{}");
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), "texto");
        return path;
    }

    [Fact]
    public void Deletes_what_is_older_than_the_days_given_with_everything_that_belongs_to_it()
    {
        var history = new CaptureHistory(_folder);
        string old = Aged("2026-09-01_10-00-00-000");
        string recent = Aged("2026-11-08_10-00-00-000");

        history.DeleteOlderThan(30, new DateTime(2026, 11, 10, 12, 0, 0));

        Assert.False(File.Exists(old));
        Assert.False(File.Exists(Path.ChangeExtension(old, ".json")));
        Assert.False(File.Exists(Path.ChangeExtension(old, ".txt")));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(Path.ChangeExtension(recent, ".txt")));
    }

    [Fact]
    public void A_capture_taken_exactly_on_the_limit_day_is_kept_and_a_copy_suffix_does_not_hide_its_age()
    {
        var history = new CaptureHistory(_folder);
        string onLimit = Aged("2026-10-11_12-00-00-000");   // 30 days before the check, to the millisecond
        string suffixed = Aged("2026-08-01_09-00-00-000_1"); // taken in the same millisecond as another one

        history.DeleteOlderThan(30, new DateTime(2026, 11, 10, 12, 0, 0));

        Assert.True(File.Exists(onLimit));
        Assert.False(File.Exists(suffixed));
    }

    [Fact]
    public void Zero_days_deletes_nothing_and_a_name_without_a_time_is_left_alone()
    {
        var history = new CaptureHistory(_folder);
        string old = Aged("2020-01-01_10-00-00-000");
        string odd = Aged("copia-a-mano");
        File.Move(odd, Path.Combine(_folder, "Zarpazo_copia-a-mano-que-no-es-una-fecha.png"));

        history.DeleteOlderThan(0, DateTime.Now);
        history.DeleteOlderThan(30, DateTime.Now);

        Assert.False(File.Exists(old)); // 30 days does reach it
        Assert.True(File.Exists(Path.Combine(_folder, "Zarpazo_copia-a-mano-que-no-es-una-fecha.png")));
    }

    [Fact]
    public void An_age_the_window_cannot_show_becomes_never()
    {
        var settings = new AppSettings { HistoryMaxAgeDays = 45 };

        settings.Normalize();

        Assert.Equal(0, settings.HistoryMaxAgeDays);
    }

    [Fact]
    public void A_kept_capture_survives_the_count_limit_and_does_not_use_up_a_place()
    {
        var history = new CaptureHistory(_folder);
        string first = history.Add(Image(1, 1), limit: 2);
        history.SetKept(first, true);
        string[] later = Enumerable.Range(2, 4).Select(i => history.Add(Image(i, 1), limit: 2)).ToArray();

        string[] listed = history.List().Select(entry => entry.Path).ToArray();

        Assert.Contains(first, listed);
        Assert.Equal(later.TakeLast(2).Reverse().Append(first).Count(), listed.Length); // the kept one plus the two newest
        Assert.True(history.List().Single(entry => entry.Path == first).Kept);
    }

    [Fact]
    public void A_kept_capture_survives_the_age_limit_until_it_is_released()
    {
        var history = new CaptureHistory(_folder);
        string old = Aged("2026-01-01_10-00-00-000");
        history.SetKept(old, true);
        var now = new DateTime(2026, 11, 10, 12, 0, 0);

        history.DeleteOlderThan(30, now);
        Assert.True(File.Exists(old));

        history.SetKept(old, false);
        Assert.False(history.IsKept(old));
        history.DeleteOlderThan(30, now);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void A_kept_recording_is_not_counted_against_the_recording_limit()
    {
        var history = new CaptureHistory(_folder);
        string first = history.AddRecording(MediaFile(".gif", "first"), Image(8, 6), recordingLimit: 1);
        history.SetKept(first, true);
        Thread.Sleep(5);
        history.AddRecording(MediaFile(".gif", "second"), Image(8, 6), recordingLimit: 1);
        Thread.Sleep(5);
        history.AddRecording(MediaFile(".gif", "third"), Image(8, 6), recordingLimit: 1);

        IReadOnlyList<HistoryEntry> recordings = history.List().Where(entry => entry.IsRecording).ToList();

        Assert.Equal(2, recordings.Count); // the kept one and the newest
        Assert.Contains(recordings, entry => entry.Path == first);
    }

    [Fact]
    public void Deleting_a_kept_capture_removes_its_mark_too_and_a_missing_one_cannot_be_kept()
    {
        var history = new CaptureHistory(_folder);
        string path = history.Add(Image(8, 6), limit: 100);
        history.SetKept(path, true);
        history.Delete(path);
        Assert.False(File.Exists(Path.ChangeExtension(path, ".keep")));

        history.SetKept(path, true); // the capture is gone: no stray mark is left behind
        Assert.False(File.Exists(Path.ChangeExtension(path, ".keep")));
    }
}
