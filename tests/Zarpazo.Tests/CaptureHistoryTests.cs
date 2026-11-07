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
}
