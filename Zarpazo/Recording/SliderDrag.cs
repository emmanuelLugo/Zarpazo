using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Zarpazo.Recording;

/// <summary>
/// Lets a slider be dragged from anywhere on it, not only by its knob: pressing puts the value under the pointer and the
/// value follows the pointer until the button is let go, even outside the slider. The editor's setting rows use it, like
/// Recordly's, where the whole row is the handle.
/// </summary>
internal static class SliderDrag
{
    public static readonly DependencyProperty AnywhereProperty = DependencyProperty.RegisterAttached(
        "Anywhere", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false, OnAnywhereChanged));

    public static bool GetAnywhere(DependencyObject element) => (bool)element.GetValue(AnywhereProperty);

    public static void SetAnywhere(DependencyObject element, bool value) => element.SetValue(AnywhereProperty, value);

    private static void OnAnywhereChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Slider slider)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            slider.PreviewMouseLeftButtonDown += OnDown;
            slider.PreviewMouseMove += OnMove;
            slider.PreviewMouseLeftButtonUp += OnUp;
        }
        else
        {
            slider.PreviewMouseLeftButtonDown -= OnDown;
            slider.PreviewMouseMove -= OnMove;
            slider.PreviewMouseLeftButtonUp -= OnUp;
        }
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (!slider.IsEnabled || slider.Template?.FindName("PART_Track", slider) is not Track)
        {
            return;
        }

        slider.CaptureMouse();
        Follow(slider, e);
        e.Handled = true; // the slider's own handling (a step per click, the knob's drag) is not wanted on top
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Follow(slider, e);
            e.Handled = true;
        }
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured)
        {
            slider.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // The value under the pointer: the track turns a point into a value, clamped to the slider's range.
    private static void Follow(Slider slider, MouseEventArgs e)
    {
        if (slider.Template.FindName("PART_Track", slider) is Track track)
        {
            double value = track.ValueFromPoint(e.GetPosition(track));
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }
    }
}
