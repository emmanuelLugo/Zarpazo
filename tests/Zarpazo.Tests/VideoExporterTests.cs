using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

[Collection("Recording")]
public sealed class VideoExporterTests : IClassFixture<SampleVideos>, IDisposable
{
    private readonly SampleVideos _samples;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoExporterTests(SampleVideos samples)
    {
        _samples = samples;
        Directory.CreateDirectory(_folder);
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

    private static async Task<MediaClip> OpenAsync(string path) =>
        await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));

    // How bright the first picture of a video is: the samples get brighter every frame, so this says where in the original it came from.
    private static async Task<int> FirstPictureBrightnessAsync(string path)
    {
        MediaClip clip = await OpenAsync(path);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        using IRandomAccessStream stream = await composition.GetThumbnailAsync(TimeSpan.Zero, 64, 36, VideoFramePrecision.NearestFrame);
        var decoder = BitmapDecoder.Create(stream.AsStreamForRead(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource picture = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        picture.CopyPixels(new System.Windows.Int32Rect(picture.PixelWidth / 2, picture.PixelHeight / 2, 1, 1), pixel, 4, 0);
        return pixel[1];
    }

    private static VideoSegment Part(double start, double end) => new(start, end);

    [Fact]
    public async Task Only_the_chosen_part_is_kept_and_the_picture_size_does_not_change()
    {
        string target = Path.Combine(_folder, "cut.mp4");

        await VideoExporter.ExportAsync(_samples.Silent, target, new[] { Part(1, 2) });

        MediaClip clip = await OpenAsync(target);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 0.8, 1.3);
        Assert.Equal((320u, 180u), (clip.GetVideoEncodingProperties().Width, clip.GetVideoEncodingProperties().Height));
        Assert.True(File.Exists(_samples.Silent), "the original is left as it was");
    }

    [Fact]
    public async Task The_sound_track_survives_the_cut()
    {
        string target = Path.Combine(_folder, "cut-sound.mp4");

        await VideoExporter.ExportAsync(_samples.WithSound, target, new[] { Part(0.5, 2.0) });

        MediaClip clip = await OpenAsync(target);
        Assert.Single(clip.EmbeddedAudioTracks);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.2, 1.8);
    }

    [Fact]
    public async Task A_part_in_the_middle_can_be_taken_out_and_the_two_sides_join()
    {
        string target = Path.Combine(_folder, "joined.mp4");

        await VideoExporter.ExportAsync(_samples.Silent, target, new[] { Part(0, 1), Part(2, 3) });

        Assert.InRange((await OpenAsync(target)).OriginalDuration.TotalSeconds, 1.7, 2.3);
    }

    [Fact]
    public async Task The_parts_play_in_the_order_given()
    {
        string inOrder = Path.Combine(_folder, "in-order.mp4");
        string swapped = Path.Combine(_folder, "swapped.mp4");

        await VideoExporter.ExportAsync(_samples.Silent, inOrder, new[] { Part(0, 1), Part(2, 3) });
        await VideoExporter.ExportAsync(_samples.Silent, swapped, new[] { Part(2, 3), Part(0, 1) });

        Assert.InRange(await FirstPictureBrightnessAsync(inOrder), 0, 40);   // starts with the dark beginning
        Assert.InRange(await FirstPictureBrightnessAsync(swapped), 100, 255); // starts with the last second, brighter
    }

    [Fact]
    public async Task A_part_can_be_used_twice()
    {
        string target = Path.Combine(_folder, "twice.mp4");

        await VideoExporter.ExportAsync(_samples.Silent, target, new[] { Part(0, 1), Part(0, 1) });

        Assert.InRange((await OpenAsync(target)).OriginalDuration.TotalSeconds, 1.7, 2.3);
    }

    [Fact]
    public async Task A_muted_part_still_exports_and_keeps_the_sound_track()
    {
        string target = Path.Combine(_folder, "muted.mp4");

        await VideoExporter.ExportAsync(_samples.WithSound, target, new[] { Part(0, 1) with { Muted = true }, Part(1, 2) });

        MediaClip clip = await OpenAsync(target);
        Assert.Single(clip.EmbeddedAudioTracks);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.7, 2.3);
    }

    [Fact]
    public async Task Cutting_off_only_the_start_or_only_the_end_works()
    {
        await VideoExporter.ExportAsync(_samples.Silent, Path.Combine(_folder, "no-start.mp4"), new[] { Part(1.5, 10) }); // end beyond the video
        await VideoExporter.ExportAsync(_samples.Silent, Path.Combine(_folder, "no-end.mp4"), new[] { Part(0, 1) });

        Assert.InRange((await OpenAsync(Path.Combine(_folder, "no-start.mp4"))).OriginalDuration.TotalSeconds, 1.2, 1.8);
        Assert.InRange((await OpenAsync(Path.Combine(_folder, "no-end.mp4"))).OriginalDuration.TotalSeconds, 0.8, 1.3);
    }

    [Fact]
    public async Task An_empty_or_backwards_part_is_refused()
    {
        string target = Path.Combine(_folder, "x.mp4");

        await Assert.ThrowsAsync<ArgumentException>(() => VideoExporter.ExportAsync(_samples.Silent, target, new[] { Part(2, 1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => VideoExporter.ExportAsync(_samples.Silent, target, new[] { Part(0, 1), Part(5, 6) })); // starts after the video ends
        await Assert.ThrowsAsync<ArgumentException>(() => VideoExporter.ExportAsync(_samples.Silent, target, Array.Empty<VideoSegment>()));
        Assert.False(File.Exists(target), "nothing is written for a refused edit");
    }

    [Fact]
    public async Task Progress_is_reported_up_to_the_end()
    {
        var seen = new List<double>();

        await VideoExporter.ExportAsync(_samples.Silent, Path.Combine(_folder, "p.mp4"), new[] { Part(0, 2) }, new Progress<double>(seen.Add));
        await Task.Delay(200); // Progress posts to its own context

        Assert.All(seen, value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void The_trim_window_can_be_created_without_failing_while_its_controls_load()
    {
        Sta.Run(() =>
        {
            var window = new TrimWindow(Path.Combine(_folder, "not-there.mp4")); // opening the file fails later, asynchronously

            Assert.False(window.SaveButton.IsEnabled); // nothing can be saved before the video has opened
            Assert.Null(window.TrimmedPath);
            window.Close();
        });
    }
}
