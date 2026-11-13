using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class RecordingBarTests
{
    private static readonly AudioMeter[] Sources = [new("Micrófono", true, 0.1f), new("Sonido de Windows", false, 0.3f)];

    private static (Path Icon, StackPanel Group) Source(RecordingBar bar, int index)
    {
        var group = (StackPanel)bar.MetersPanel.Children[index];
        return ((Path)group.Children[0], group);
    }

    private static void Click(UIElement element) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
    {
        RoutedEvent = UIElement.MouseLeftButtonDownEvent,
        Source = element,
    });

    private static void Wheel(UIElement element, int delta) => element.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
    {
        RoutedEvent = UIElement.MouseWheelEvent,
        Source = element,
    });

    [Fact]
    public void There_is_one_meter_per_sound_source()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();

            bar.ShowRecording(TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(30), Sources);

            Assert.Equal(2, bar.MetersPanel.Children.Count);
        });
    }

    [Fact]
    public void Clicking_a_source_mutes_it_and_clicking_again_brings_back_the_volume_it_had()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();
            var changes = new List<(int Source, float Gain)>();
            bar.GainChanged += (source, gain) => changes.Add((source, gain));
            bar.ShowRecording(TimeSpan.Zero, TimeSpan.FromMinutes(30), Sources);
            (Path icon, StackPanel group) = Source(bar, 1);

            Wheel(group, 120);   // 110 %
            Click(icon);          // muted
            Click(icon);          // back to 110 %, not to 100

            Assert.Equal([(1, 1.1f), (1, 0f), (1, 1.1f)], changes);
        });
    }

    [Fact]
    public void The_wheel_changes_the_volume_in_steps_of_ten_percent_between_nothing_and_double()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();
            var gains = new List<float>();
            bar.GainChanged += (_, gain) => gains.Add(gain);
            bar.ShowRecording(TimeSpan.Zero, TimeSpan.FromMinutes(30), Sources);
            (_, StackPanel group) = Source(bar, 0);

            for (int i = 0; i < 15; i++)
            {
                Wheel(group, 120); // far more than needed to reach double
            }
            for (int i = 0; i < 30; i++)
            {
                Wheel(group, -120); // and then all the way down
            }

            Assert.Equal(2f, gains.Max());
            Assert.Equal(0f, gains.Min());
            Assert.Equal(0f, gains[^1]);
            Assert.All(gains, gain => Assert.Equal(Math.Round(gain * 10) / 10, gain, 4)); // always tidy tenths
        });
    }

    [Fact]
    public void A_muted_source_is_shown_dimmed_with_a_muted_icon()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();
            bar.ShowRecording(TimeSpan.Zero, TimeSpan.FromMinutes(30), Sources);
            (Path icon, StackPanel group) = Source(bar, 0);
            Geometry normal = icon.Data;

            Click(icon);

            Assert.Same(RecordingBar.MicrophoneOffGlyph, icon.Data);
            Assert.NotSame(normal, icon.Data);
            Assert.True(group.Opacity < 1);
        });
    }

    [Fact]
    public void A_source_that_arrives_already_muted_stays_muted()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();

            bar.ShowRecording(TimeSpan.Zero, TimeSpan.FromMinutes(30), [new AudioMeter("Micrófono", true, 0f, Gain: 0f)]);

            Assert.True(Source(bar, 0).Group.Opacity < 1);
        });
    }
    [Fact]
    public void While_paused_the_bar_says_so_and_the_button_offers_to_carry_on()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();

            bar.ShowRecording(TimeSpan.FromSeconds(9), TimeSpan.FromMinutes(30), Sources, paused: true);

            Assert.Equal("PAUSA", bar.StatusText.Text);
            Assert.Same(RecordingBar.ResumeGlyph, bar.PauseIcon.Data);
            Assert.Equal("00:09", bar.ElapsedText.Text);

            bar.ShowRecording(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(30), Sources);

            Assert.Equal("REC", bar.StatusText.Text);
            Assert.Same(RecordingBar.PauseGlyph, bar.PauseIcon.Data);
        });
    }

    [Fact]
    public void Without_sound_sources_there_is_no_empty_group_for_them()
    {
        Sta.Run(() =>
        {
            var bar = new RecordingBar();

            bar.ShowRecording(TimeSpan.Zero, TimeSpan.FromMinutes(30), []);

            Assert.Equal(Visibility.Collapsed, bar.MetersDivider.Visibility);
        });
    }

    // Draws every state of the bar to PNG files when ZARPAZO_SCREENSHOTS names a folder, to look at the design without recording.
    [Fact]
    public void Each_state_can_be_drawn()
    {
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        Sta.Run(() =>
        {
            var bar = new RecordingBar();
            bar.ShowCountdown(3);
            Shot(bar, folder, "bar-countdown.png");
            bar.ShowRecording(TimeSpan.FromSeconds(9), TimeSpan.FromMinutes(30), Sources);
            Shot(bar, folder, "bar-recording.png");
            bar.ShowRecording(TimeSpan.FromSeconds(9), TimeSpan.FromMinutes(30), [Sources[0] with { Gain = 0f }], paused: true);
            Shot(bar, folder, "bar-paused.png");
            bar.ShowEncoding("Creando el video…");
            Shot(bar, folder, "bar-encoding.png");
            bar.ShowDone("GIF listo · 2,4 MB");
            Shot(bar, folder, "bar-done.png");
            bar.ShowFailure("La grabación fue demasiado corta.");
            Shot(bar, folder, "bar-failure.png");
        });
    }

    private static void Shot(RecordingBar bar, string? folder, string name)
    {
        var content = (FrameworkElement)bar.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        Assert.True(content.DesiredSize.Width > 0);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        // Twice the size, on a grey like a desktop, so the shadow and the edges can be judged.
        const double scale = 2;
        var desk = new DrawingVisual();
        using (DrawingContext context = desk.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x42)), null, new Rect(content.DesiredSize));
        }
        var target = new RenderTargetBitmap((int)(content.DesiredSize.Width * scale), (int)(content.DesiredSize.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(desk);
        target.Render(content); // on top of the grey, at its own size (a VisualBrush would stretch it to its shadow's bounds)
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        System.IO.Directory.CreateDirectory(folder);
        using System.IO.FileStream stream = System.IO.File.Create(System.IO.Path.Combine(folder, name));
        encoder.Save(stream);
    }
}
