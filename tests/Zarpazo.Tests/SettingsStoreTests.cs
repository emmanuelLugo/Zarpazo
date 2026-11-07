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
}
