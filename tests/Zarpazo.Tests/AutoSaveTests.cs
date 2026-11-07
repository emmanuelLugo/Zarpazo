using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

public sealed class AutoSaveTests : IDisposable
{
    private static readonly DateTime Moment = new(2026, 11, 7, 14, 30, 5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static BitmapSource Picture() =>
        BitmapSource.Create(20, 10, 96, 96, PixelFormats.Bgr32, null, new byte[20 * 10 * 4], 20 * 4);

    private SettingsStore Store(Action<AppSettings>? change = null)
    {
        var store = new SettingsStore(Path.Combine(_root, "settings.json"));
        store.Current.AutoSave = true;
        store.Current.AutoSaveFolder = Path.Combine(_root, "saved");
        change?.Invoke(store.Current);
        return store;
    }

    // ---------- the template

    [Fact]
    public void The_default_template_gives_the_date_and_time()
    {
        Assert.Equal("Zarpazo 2026-11-07 14-30-05", FileNameTemplate.Expand(FileNameTemplate.Default, Moment, 1));
    }

    [Theory]
    [InlineData("{fecha}", "2026-11-07")]
    [InlineData("Captura {hora}", "Captura 14-30-05")]
    [InlineData("{FECHA}_{Hora}", "2026-11-07_14-30-05")]
    [InlineData("pantalla {n}", "pantalla 7")]
    [InlineData("pantalla {n:000}", "pantalla 007")]
    [InlineData("{n:00}-{fecha}", "07-2026-11-07")]
    public void The_tokens_are_filled_in(string template, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.Expand(template, Moment, 7));
    }

    [Fact]
    public void A_number_longer_than_the_padding_is_not_cut()
    {
        Assert.Equal("1234", FileNameTemplate.Expand("{n:00}", Moment, 1234));
    }

    [Theory]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a-b-c-d-e-f-g-h-i-j")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("ends with dots...", "ends with dots")]
    public void What_windows_does_not_allow_in_a_name_is_taken_out(string template, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.Expand(template, Moment, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_template_falls_back_to_the_default(string? template)
    {
        Assert.Equal("Zarpazo 2026-11-07 14-30-05", FileNameTemplate.Expand(template, Moment, 1));
    }

    [Fact]
    public void A_name_that_is_all_invalid_still_gives_something_usable()
    {
        Assert.Equal("Zarpazo", FileNameTemplate.Sanitize("...  "));
    }

    [Fact]
    public void A_very_long_name_is_cut()
    {
        Assert.True(FileNameTemplate.Expand(new string('a', 500), Moment, 1).Length <= 150);
    }

    [Fact]
    public void A_name_already_taken_gets_a_number_instead_of_overwriting()
    {
        Directory.CreateDirectory(_root);

        string first = FileNameTemplate.UniquePath(_root, "shot", ".png");
        File.WriteAllText(first, "x");
        string second = FileNameTemplate.UniquePath(_root, "shot", ".png");
        File.WriteAllText(second, "x");
        string third = FileNameTemplate.UniquePath(_root, "shot", ".png");

        Assert.Equal(["shot.png", "shot (2).png", "shot (3).png"], new[] { first, second, third }.Select(Path.GetFileName));
    }

    // ---------- saving

    [Fact]
    public void A_picture_is_saved_in_the_folder_with_the_template_name_and_the_folder_is_created()
    {
        AutoSaver saver = new(Store(s => s.FileNameTemplate = "shot {n:000}"));

        string path = saver.SaveImage(Picture());

        Assert.Equal(Path.Combine(_root, "saved", "shot 001.png"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void The_counter_goes_up_with_each_save_and_is_remembered_by_the_next_run()
    {
        SettingsStore store = Store(s => s.FileNameTemplate = "n{n}");
        var saver = new AutoSaver(store);

        saver.SaveImage(Picture());
        saver.SaveImage(Picture());
        string third = saver.SaveImage(Picture());

        Assert.Equal("n3.png", Path.GetFileName(third));
        Assert.Equal(3, new SettingsStore(Path.Combine(_root, "settings.json")).Current.AutoSaveCounter);
    }

    [Fact]
    public void The_format_follows_the_setting()
    {
        AutoSaver saver = new(Store(s => s.SaveFormat = "jpg"));

        Assert.Equal(".jpg", Path.GetExtension(saver.SaveImage(Picture())));
    }

    [Fact]
    public void Two_saves_with_the_same_name_do_not_overwrite_each_other()
    {
        AutoSaver saver = new(Store(s => s.FileNameTemplate = "same")); // no counter in the name

        string first = saver.SaveImage(Picture());
        string second = saver.SaveImage(Picture());

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first) && File.Exists(second));
    }

    [Fact]
    public void A_recording_is_copied_keeping_its_extension()
    {
        Directory.CreateDirectory(_root);
        string video = Path.Combine(_root, "recording.mp4");
        File.WriteAllText(video, "video bytes");
        AutoSaver saver = new(Store(s => s.FileNameTemplate = "clip {n}"));

        string path = saver.SaveFile(video);

        Assert.Equal("clip 1.mp4", Path.GetFileName(path));
        Assert.Equal("video bytes", File.ReadAllText(path));
        Assert.True(File.Exists(video), "the original is left in place");
    }

    [Fact]
    public void With_no_folder_chosen_it_uses_pictures_zarpazo()
    {
        var settings = new AppSettings { AutoSaveFolder = "" };

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Zarpazo"), AutoSaver.FolderFor(settings));
    }

    [Fact]
    public void It_is_off_until_asked_for()
    {
        Assert.False(new AutoSaver(new SettingsStore(Path.Combine(_root, "fresh.json"))).IsEnabled);
    }

    // ---------- the settings

    [Fact]
    public void The_auto_save_choices_are_read_back_and_a_blank_template_in_the_file_means_the_default()
    {
        SettingsStore first = Store(s => s.FileNameTemplate = "mine {n}");
        first.Save();
        AppSettings second = new SettingsStore(Path.Combine(_root, "settings.json")).Current;

        Assert.True(second.AutoSave);
        Assert.Equal(Path.Combine(_root, "saved"), second.AutoSaveFolder);
        Assert.Equal("mine {n}", second.FileNameTemplate);

        File.WriteAllText(Path.Combine(_root, "settings.json"), """{ "FileNameTemplate": "  ", "AutoSaveCounter": -5 }""");
        AppSettings repaired = new SettingsStore(Path.Combine(_root, "settings.json")).Current;
        Assert.Equal(FileNameTemplate.Default, repaired.FileNameTemplate);
        Assert.Equal(0, repaired.AutoSaveCounter);
    }
}
