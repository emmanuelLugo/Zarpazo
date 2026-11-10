using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Zarpazo.Recording;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Guide;

/// <summary>
/// The small bar shown while a guide is being recorded by clicking: how many steps there are and a button to finish.
/// Kept out of the pictures, and its own clicks never become steps.
/// </summary>
internal sealed class ClickBar : Window
{
    private const int BottomGapPixels = 90; // above the taskbar

    private readonly TextBlock _count = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 14, 0),
    };

    public ClickBar(int steps)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24));

        var finish = new Button
        {
            Content = "Terminar",
            Focusable = false,
            Padding = new Thickness(14, 4, 14, 4),
            Cursor = Cursors.Hand,
        };
        finish.Click += (_, _) => FinishRequested?.Invoke(this, EventArgs.Empty);

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 8, 10, 8),
            Children =
            {
                new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                },
                _count,
                finish,
            },
        };

        ShowSteps(steps);
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowNative.ExcludeFromScreenCapture(hwnd);
        Loaded += (_, _) => PlaceAtBottom();
    }

    public event EventHandler? FinishRequested;

    /// <summary>The bar's place on the screen in physical pixels; clicks inside it are not steps.</summary>
    public Rectangle Bounds => WindowNative.Bounds(new WindowInteropHelper(this).Handle);

    public void ShowSteps(int steps) =>
        _count.Text = steps == 0 ? "Grabando guía: haz clic para añadir pasos" : $"Grabando guía · {steps} {(steps == 1 ? "paso" : "pasos")}";

    // Physical pixels, because WPF's own Left/Top can't describe a position on a monitor with another scaling.
    private void PlaceAtBottom()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Matrix toDevice = PresentationSource.FromVisual(this)!.CompositionTarget!.TransformToDevice;
        int width = (int)(ActualWidth * toDevice.M11);
        int height = (int)(ActualHeight * toDevice.M22);
        WindowNative.Place(hwnd, new Rectangle(area.X + (area.Width - width) / 2, area.Bottom - height - BottomGapPixels, width, height));
    }
}
