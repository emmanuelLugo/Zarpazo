using System.IO;
using Zarpazo.Recording;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

/// <summary>How the finished video is written: its size, quality and frame rate, and how they are remembered.</summary>
public sealed class VideoExportOptionsTests : IDisposable
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

    [Fact]
    public void The_default_is_what_was_always_written()
    {
        VideoExportOptions options = VideoExportOptions.Default;

        Assert.True(options.IsDefault);
        Assert.Equal((1920, 1080), options.SizeFor(1920, 1080));
        Assert.Equal(1, options.Scale);
        Assert.Equal(1, options.QualityFactor);
        Assert.Equal(0, options.Fps); // as recorded
        Assert.False((options with { Quality = ExportQuality.Light }).IsDefault);
    }

    [Theory]
    [InlineData("High", 1920, 1080, 1728, 972)]
    [InlineData("Medium", 1920, 1080, 1440, 810)]
    [InlineData("Low", 1920, 1080, 960, 540)]
    [InlineData("Low", 1366, 768, 682, 384)] // H.264 needs even sides: 683 becomes 682
    [InlineData("High", 1918, 1078, 1726, 970)]
    [InlineData("Low", 4, 2, 2, 2)] // never smaller than a block
    public void A_smaller_size_keeps_the_proportion_and_both_sides_even(string resolution, int width, int height, int expectedWidth, int expectedHeight)
    {
        var options = new VideoExportOptions(Enum.Parse<ExportResolution>(resolution), ExportQuality.Balanced, 0);

        (int outWidth, int outHeight) = options.SizeFor(width, height);

        Assert.Equal((expectedWidth, expectedHeight), (outWidth, outHeight));
        Assert.Equal(0, outWidth % 2);
        Assert.Equal(0, outHeight % 2);
    }

    [Fact]
    public void Quality_goes_from_light_to_high_around_the_balanced_one()
    {
        double light = new VideoExportOptions(ExportResolution.Original, ExportQuality.Light, 0).QualityFactor;
        double high = new VideoExportOptions(ExportResolution.Original, ExportQuality.High, 0).QualityFactor;

        Assert.True(light < 1);
        Assert.True(high > 1);
    }

    [Fact]
    public void Odd_values_are_brought_back_to_what_is_offered()
    {
        var odd = new VideoExportOptions((ExportResolution)42, (ExportQuality)(-3), 45);

        Assert.Equal(VideoExportOptions.Default, odd.Tidied());
        Assert.Equal(new VideoExportOptions(ExportResolution.Low, ExportQuality.High, 60),
            new VideoExportOptions(ExportResolution.Low, ExportQuality.High, 60).Tidied());
    }

    [Fact]
    public void The_choice_is_read_back_by_the_next_run_and_a_hand_edited_file_is_tidied()
    {
        var first = new SettingsStore(FilePath);
        first.Current.VideoExport = new VideoExportOptions(ExportResolution.Medium, ExportQuality.Light, 24);
        first.Save();
        Assert.Equal(new VideoExportOptions(ExportResolution.Medium, ExportQuality.Light, 24), new SettingsStore(FilePath).Current.VideoExport);

        File.WriteAllText(FilePath, """{ "VideoExportResolution": "Enorme", "VideoExportQuality": "Light", "VideoExportFps": 17 }""");
        VideoExportOptions tidied = new SettingsStore(FilePath).Current.VideoExport;

        Assert.Equal(new VideoExportOptions(ExportResolution.Original, ExportQuality.Light, 0), tidied);
    }
}
