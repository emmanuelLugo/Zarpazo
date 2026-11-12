using System.IO;
using System.Windows;
using Zarpazo.Settings;
using Zarpazo.Shell;
using Zarpazo.Theme;

namespace Zarpazo.Tests;

public sealed class ThemeTests
{
    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Theory]
    [InlineData("System", false, false)]
    [InlineData("System", true, true)]
    [InlineData("Light", true, false)]
    [InlineData("Dark", false, true)]
    public void A_mode_resolves_to_the_colours_it_asks_for(string mode, bool windowsIsDark, bool expectDark)
    {
        Assert.Equal(expectDark, ThemeManager.Resolve(Enum.Parse<ThemeMode>(mode), windowsIsDark));
    }

    [Fact]
    public void Light_and_dark_define_exactly_the_same_colours()
    {
        Sta.Run(() =>
        {
            // Loading by pack address only works once WPF has registered that scheme, which otherwise depends on whether another
            // test created a window first. Touching this registers it, whatever the order the tests run in.
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            // Not enough on its own: the request handler for pack addresses is registered when WPF's Application class
            // starts up, which the other tests in a run may not have caused.
            _ = System.Windows.Application.Current;

            ResourceDictionary Load(string name) => new() { Source = new Uri($"/Zarpazo;component/Theme/{name}.xaml", UriKind.Relative) };

            var light = Load("Light").Keys.Cast<object>().Select(key => key.ToString()!).Order().ToArray();
            var dark = Load("Dark").Keys.Cast<object>().Select(key => key.ToString()!).Order().ToArray();

            Assert.NotEmpty(light);
            Assert.Equal(light, dark); // a colour missing in one theme would show as nothing in that theme
        });
    }

    [Fact]
    public void The_chosen_appearance_is_kept_between_runs()
    {
        string path = NewSettingsPath();
        var store = new SettingsStore(path);
        Assert.Equal(ThemeMode.System, store.Current.AppearanceMode); // the default follows Windows

        store.Current.AppearanceMode = ThemeMode.Dark;
        store.Save();

        Assert.Equal(ThemeMode.Dark, new SettingsStore(path).Current.AppearanceMode);
    }

    [Fact]
    public void An_unknown_appearance_in_the_file_falls_back_to_following_windows()
    {
        string path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Appearance": "purple" }""");

        Assert.Equal(ThemeMode.System, new SettingsStore(path).Current.AppearanceMode);
        Assert.Equal("system", new SettingsStore(path).Current.Appearance);
    }

    [Fact]
    public void The_settings_window_shows_the_saved_appearance()
    {
        Sta.Run(() =>
        {
            var store = new SettingsStore(NewSettingsPath());
            store.Current.AppearanceMode = ThemeMode.Dark;

            var window = new SettingsWindow(store, new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            Assert.Equal((int)ThemeMode.Dark, window.ThemeBox.SelectedIndex);
            window.Close();
        });
    }
}
