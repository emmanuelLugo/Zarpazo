using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [Fact]
    public void A_hand_edited_limit_below_one_falls_back_to_the_default()
    {
        var settings = new AppSettings { HistoryLimit = 0 };

        settings.Normalize();

        Assert.Equal(AppSettings.DefaultHistoryLimit, settings.HistoryLimit);
    }
}
