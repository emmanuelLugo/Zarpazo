using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>A dashed frame just outside the area being recorded, so it is clear what is in the picture.</summary>
internal sealed class RecordingOutline : Window
{
    private const int RingPixels = 4; // outside the area, so the frame never ends up in the recording itself

    public RecordingOutline(Rectangle region)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        // A thin white line under the red one, so the frame shows on both light and dark content.
        var frame = new Grid
        {
            Children =
            {
                new System.Windows.Shapes.Rectangle { Margin = new Thickness(1), Stroke = Brushes.White, StrokeThickness = 3 },
                new System.Windows.Shapes.Rectangle
                {
                    Margin = new Thickness(1),
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35)),
                    StrokeThickness = 2,
                    StrokeDashArray = [4, 3],
                },
            },
        };
        Content = frame;

        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowNative.MakeClickThrough(hwnd);
        WindowNative.KeepPixelSize(hwnd);
        // Drawn in pixels whatever the scaling, so the lines stay within the ring and out of the recording.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        frame.LayoutTransform = new ScaleTransform(1 / scale, 1 / scale);
        WindowNative.Place(hwnd, Rectangle.Inflate(region, RingPixels, RingPixels));
    }
}
