using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class RecordingBarTests
{
    private static readonly AudioMeter[] Sources = [new("Micrófono", true, 0.1f), new("Sonido de Windows", false, 0.3f)];

    private static (TextBlock Icon, StackPanel Group) Source(RecordingBar bar, int index)
    {
        var group = (StackPanel)bar.MetersPanel.Children[index];
        return ((TextBlock)group.Children[0], group);
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
            (TextBlock icon, StackPanel group) = Source(bar, 1);

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
            (TextBlock icon, StackPanel group) = Source(bar, 0);
            string normal = icon.Text;

            Click(icon);

            Assert.NotEqual(normal, icon.Text);
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
}
