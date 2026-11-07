using System.IO;
using Zarpazo.Settings;
using Zarpazo.Shell;

namespace Zarpazo.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

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
    public void Starts_with_the_defaults_when_there_is_no_file()
    {
        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(HotKeyBinding.Default, settings.HotKey);
        Assert.True(settings.CopyAsFile);
        Assert.Equal("png", settings.SaveFormat);
        Assert.True(settings.RememberStyle);
    }

    [Fact]
    public void What_is_saved_is_read_back_by_the_next_run()
    {
        var first = new SettingsStore(FilePath);
        first.Current.HotKey = new HotKeyBinding(HotKeyBinding.Control | HotKeyBinding.Shift, 0x53);
        first.Current.CopyAsFile = false;
        first.Current.SaveFolder = @"D:\Capturas";
        first.Current.SaveFormat = "jpg";
        first.Current.Color = "#1E88E5";
        first.Current.Thickness = 8;
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;

        Assert.Equal(first.Current.HotKey, second.HotKey);
        Assert.False(second.CopyAsFile);
        Assert.Equal(@"D:\Capturas", second.SaveFolder);
        Assert.Equal("jpg", second.SaveFormat);
        Assert.Equal("#1E88E5", second.Color);
        Assert.Equal(8, second.Thickness);
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults_instead_of_failing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ this is not json");

        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(HotKeyBinding.Default, settings.HotKey);
    }

    [Fact]
    public void Values_that_would_break_things_fall_back_to_the_defaults()
    {
        Directory.CreateDirectory(_folder);
        // Letter A alone as the shortcut would steal typing; "bmp" is not a format the Save dialog offers.
        File.WriteAllText(FilePath, """{ "HotKeyModifiers": 0, "HotKeyVirtualKey": 65, "SaveFormat": "bmp", "SaveFolder": null }""");

        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(HotKeyBinding.Default, settings.HotKey);
        Assert.Equal("png", settings.SaveFormat);
        Assert.Equal("", settings.SaveFolder);
    }

    [Fact]
    public void Saving_creates_the_folder()
    {
        var store = new SettingsStore(FilePath);

        store.Save();

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Beautify_starts_off_with_the_default_style()
    {
        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.False(settings.BeautifyEnabled);
        Assert.Equal(Zarpazo.Output.BeautifyStyle.Default, settings.BeautifyStyle);
    }

    [Fact]
    public void The_beautify_choices_are_read_back_by_the_next_run()
    {
        var first = new SettingsStore(FilePath);
        first.Current.BeautifyEnabled = true;
        first.Current.BeautifyStyle = new Zarpazo.Output.BeautifyStyle("oceano", 80, 24, Shadow: false);
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;

        Assert.True(second.BeautifyEnabled);
        Assert.Equal(new Zarpazo.Output.BeautifyStyle("oceano", 80, 24, Shadow: false), second.BeautifyStyle);
    }

    [Fact]
    public void Hand_edited_beautify_values_snap_to_what_the_panel_offers()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "BeautifyBackground": "rosa-fuerte", "BeautifyPadding": 55, "BeautifyRadius": 500 }""");

        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(new Zarpazo.Output.BeautifyStyle("violeta", 48, 24, Shadow: true), settings.BeautifyStyle);
    }

    [Fact]
    public void Recording_starts_as_a_gif_without_sound()
    {
        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(Zarpazo.Recording.RecordingOptions.Default, settings.RecordingOptions);
    }

    [Fact]
    public void The_recording_choices_are_read_back_by_the_next_run()
    {
        var first = new SettingsStore(FilePath);
        first.Current.RecordingOptions = new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, Microphone: true, SystemSound: true);
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;

        Assert.Equal(new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, true, true), second.RecordingOptions);
    }

    [Fact]
    public void An_unknown_recording_format_in_the_file_means_gif()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "RecordFormat": "avi", "RecordMicrophone": true }""");

        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.Equal(Zarpazo.Recording.RecordingFormat.Gif, settings.RecordingOptions.Format);
    }

    [Fact]
    public void The_chosen_microphone_is_read_back_by_the_next_run()
    {
        var first = new SettingsStore(FilePath);
        first.Current.RecordingOptions = new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, true, false, "{0.0.1.00000000}.{abc}");
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;

        Assert.Equal("{0.0.1.00000000}.{abc}", second.RecordingOptions.MicrophoneId);
    }

    [Fact]
    public void The_countdown_starts_at_three_seconds_and_odd_values_snap_to_what_is_offered()
    {
        Assert.Equal(3, new SettingsStore(FilePath).Current.RecordCountdownSeconds);

        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "RecordCountdownSeconds": 9 }""");

        Assert.Equal(5, new SettingsStore(FilePath).Current.RecordCountdownSeconds);
    }

    [Fact]
    public void The_recording_shortcut_starts_as_ctrl_alt_r_and_is_read_back()
    {
        var first = new SettingsStore(FilePath);
        Assert.Equal(HotKeyBinding.DefaultRecord, first.Current.RecordHotKey);

        first.Current.RecordHotKey = new HotKeyBinding(HotKeyBinding.Control | HotKeyBinding.Shift, 0x47);
        first.Save();

        Assert.Equal(new HotKeyBinding(HotKeyBinding.Control | HotKeyBinding.Shift, 0x47), new SettingsStore(FilePath).Current.RecordHotKey);
    }

    [Fact]
    public void A_recording_shortcut_that_would_break_typing_or_repeats_the_capture_one_falls_back_to_the_default()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "RecordHotKeyModifiers": 0, "RecordHotKeyVirtualKey": 82 }"""); // the letter R alone
        Assert.Equal(HotKeyBinding.DefaultRecord, new SettingsStore(FilePath).Current.RecordHotKey);

        // The same combination as the capture shortcut (Ctrl+Alt+R for both).
        File.WriteAllText(FilePath, """{ "HotKeyModifiers": 3, "HotKeyVirtualKey": 82, "RecordHotKeyModifiers": 3, "RecordHotKeyVirtualKey": 82 }""");
        AppSettings settings = new SettingsStore(FilePath).Current;

        Assert.NotEqual(settings.HotKey, settings.RecordHotKey);
    }

    [Fact]
    public void The_sound_sync_offset_and_output_device_are_read_back_and_odd_offsets_snap_to_the_offered_ones()
    {
        var first = new SettingsStore(FilePath);
        first.Current.RecordAudioOffsetMs = -100;
        first.Current.RecordingOptions = new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, false, true, "", "speakers-id");
        first.Save();

        AppSettings second = new SettingsStore(FilePath).Current;
        Assert.Equal(-100, second.RecordAudioOffsetMs);
        Assert.Equal("speakers-id", second.RecordingOptions.SystemSoundId);

        File.WriteAllText(FilePath, """{ "RecordAudioOffsetMs": 90 }""");
        Assert.Equal(100, new SettingsStore(FilePath).Current.RecordAudioOffsetMs);
    }
}
