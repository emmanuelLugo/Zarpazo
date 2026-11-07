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
}
