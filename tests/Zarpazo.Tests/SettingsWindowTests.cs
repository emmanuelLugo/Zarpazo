using System.IO;
using Zarpazo.Settings;
using Zarpazo.Shell;

namespace Zarpazo.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void The_window_opens_showing_both_shortcuts_and_the_countdown()
    {
        Sta.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            var store = new SettingsStore(path);
            store.Current.RecordCountdownSeconds = 5;

            var window = new SettingsWindow(store, new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            Assert.Equal(HotKeyBinding.Default, window.CaptureHotKey.Value);
            Assert.Equal(HotKeyBinding.DefaultRecord, window.RecordHotKey.Value);
            Assert.Equal(2, window.CountdownBox.SelectedIndex); // 5 seconds
            window.Close();
        });
    }

    [Fact]
    public void The_window_shows_the_saved_sound_offset()
    {
        Sta.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            var store = new SettingsStore(path);
            store.Current.RecordAudioOffsetMs = -50;

            var window = new SettingsWindow(store, new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            Assert.Equal(2, window.AudioOffsetBox.SelectedIndex); // the third option: "adelantar 50 ms"
            window.Close();
        });
    }

    [Fact]
    public void The_window_shows_the_auto_save_choices_and_an_example_name()
    {
        Sta.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            var store = new SettingsStore(path);
            store.Current.AutoSave = true;
            store.Current.AutoSaveFolder = @"D:\Capturas";
            store.Current.FileNameTemplate = "shot {n:000}";
            store.Current.AutoSaveCounter = 41;

            var window = new SettingsWindow(store, new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            Assert.True(window.AutoSaveBox.IsChecked);
            Assert.Equal(@"D:\Capturas", window.AutoFolderBox.Text);
            Assert.Equal("shot {n:000}", window.TemplateBox.Text);
            Assert.Equal("Ejemplo: shot 042.png", window.TemplatePreview.Text); // the next number, 42
            window.Close();
        });
    }

    [Fact]
    public void The_window_shows_the_saved_quality_choices()
    {
        Sta.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            var store = new SettingsStore(path);
            store.Current.RecordingQuality = new Zarpazo.Recording.RecordingQuality(20, true, 60, 24, Zarpazo.Recording.VideoQuality.High, 10);
            store.Current.JpegQuality = 75;

            var window = new SettingsWindow(store, new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            Assert.Equal(3, window.GifFpsBox.SelectedIndex);       // 20
            Assert.Equal(2, window.GifMaxBox.SelectedIndex);       // 60 s
            Assert.True(window.GifDitherBox.IsChecked);
            Assert.Equal(1, window.VideoFpsBox.SelectedIndex);     // 24
            Assert.Equal(2, window.VideoQualityBox.SelectedIndex); // high
            Assert.Equal(1, window.VideoMaxBox.SelectedIndex);     // 10 min
            Assert.Equal(1, window.JpegQualityBox.SelectedIndex);  // 75
            window.Close();
        });
    }

    [Fact]
    public void Resetting_a_section_restores_only_that_section_and_saves_nothing()
    {
        Sta.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            var store = new SettingsStore(path);
            store.Current.ShowMagnifier = false;
            store.Current.RecordCountdownSeconds = 5;
            var window = new SettingsWindow(store, new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            window.Nav.SelectedIndex = 2; // Captura
            window.ResetPageButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.True(window.MagnifierBox.IsChecked);
            Assert.Equal(2, window.CountdownBox.SelectedIndex); // the Grabación page was not touched
            Assert.False(store.Current.ShowMagnifier); // nothing is saved until Aceptar
            window.Close();
        });
    }

    [Fact]
    public void Only_the_chosen_page_is_visible_and_the_about_page_has_no_reset_button()
    {
        Sta.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            var window = new SettingsWindow(new SettingsStore(path), new StartupRegistration(@"Software\Zarpazo.Tests", "Zarpazo", @"C:\Zarpazo\Zarpazo.exe"), _ => true, _ => true);

            Assert.Equal(System.Windows.Visibility.Visible, window.PageGeneral.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, window.PageSaving.Visibility);

            window.Nav.SelectedIndex = 4;
            Assert.Equal(System.Windows.Visibility.Visible, window.PageSaving.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, window.PageGeneral.Visibility);

            window.Nav.SelectedIndex = 5;
            Assert.Equal(System.Windows.Visibility.Collapsed, window.ResetPageButton.Visibility);
            window.Close();
        });
    }
}
