using System.IO;
using System.Windows;
using Xunit;
using Zarpazo.History;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

public class HistoryWindowTests
{
    [Fact]
    public void Opens_with_nothing_picked_so_its_actions_are_off()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Sta.Run(() =>
            {
                var settings = new SettingsStore(Path.Combine(folder, "settings.json"));
                var window = new HistoryWindow(new CaptureHistory(Path.Combine(folder, "history")), settings, _ => { });

                Assert.False(window.CopyButton.IsEnabled);
                Assert.False(window.DeleteButton.IsEnabled);
                Assert.False(window.MoreOpenItem.IsEnabled);
                Assert.True(window.MoreButton.IsEnabled); // "Abrir carpeta" needs nothing picked
                Assert.Equal("Impr Pant", window.EmptyHotKey.Text);
                Assert.Equal(Visibility.Collapsed, window.EmptyPanel.Visibility); // not before the list is read
                window.Close(); // or it keeps following the theme from a thread that no longer exists
            });
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
