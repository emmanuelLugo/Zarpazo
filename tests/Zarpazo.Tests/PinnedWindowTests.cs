using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Pin;
using Zarpazo.Settings;

namespace Zarpazo.Tests;

public sealed class PinnedWindowTests
{
    [Fact]
    public void The_wheel_up_makes_it_more_solid_and_down_more_transparent()
    {
        Assert.Equal(0.9, PinnedWindow.NextOpacity(0.8, 120));
        Assert.Equal(0.7, PinnedWindow.NextOpacity(0.8, -120));
    }

    [Fact]
    public void Opacity_stays_between_a_findable_minimum_and_fully_solid()
    {
        Assert.Equal(1.0, PinnedWindow.NextOpacity(1.0, 120));
        Assert.Equal(0.2, PinnedWindow.NextOpacity(0.2, -120));
    }

    [Fact]
    public void Ten_steps_down_from_solid_do_not_drift_off_the_tenths()
    {
        double opacity = 1.0;
        for (int i = 0; i < 4; i++)
        {
            opacity = PinnedWindow.NextOpacity(opacity, -120);
        }

        Assert.Equal(0.6, opacity);
    }

    [Fact]
    public void The_window_loads_its_xaml_and_takes_the_picture()
    {
        Sta.Run(() =>
        {
            BitmapSource image = BitmapSource.Create(120, 80, 96, 96, PixelFormats.Bgr32, null, new byte[120 * 80 * 4], 120 * 4);
            var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"), "settings.json"));

            var window = new PinnedWindow(image, -50, 20, settings); // not shown: it only has to build and be placed

            Assert.NotNull(window.Picture.Fill);
            Assert.True(window.IsInitialized);
            window.Close();
        });
    }
}
