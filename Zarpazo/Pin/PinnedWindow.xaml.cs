using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.Pin;

/// <summary>A capture left floating above everything else, where it was on the screen, to keep it in view while working.</summary>
public partial class PinnedWindow : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const double OpacityStep = 0.1;
    private const double MinOpacity = 0.2; // below this the picture can no longer be found on the screen

    private readonly BitmapSource _image;
    private readonly SettingsStore _settings;

    /// <param name="image">The capture, one image pixel per physical pixel.</param>
    /// <param name="left">Where its top-left corner goes, in physical screen pixels (can be negative).</param>
    internal PinnedWindow(BitmapSource image, int left, int top, SettingsStore settings)
    {
        InitializeComponent();
        _image = image;
        _settings = settings;
        Picture.Source = image;

        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Moving it to a monitor with another scaling would make Windows resize it; it keeps its pixel size instead.
        HwndSource.FromHwnd(hwnd).AddHook(IgnoreDpiChange);
        // Physical pixels, like the overlay: WPF's Left/Top/Width/Height can't place a window across monitors with different DPI.
        SetWindowPos(hwnd, IntPtr.Zero, left, top, image.PixelWidth, image.PixelHeight, SwpNoZOrder | SwpNoActivate);

        MouseEnter += (_, _) => CloseButton.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => CloseButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>The opacity after one step of the mouse wheel (up makes it more solid), kept within a usable range.</summary>
    internal static double NextOpacity(double current, int wheelDelta) =>
        Math.Clamp(Math.Round(current + (wheelDelta > 0 ? OpacityStep : -OpacityStep), 1), MinOpacity, 1.0);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e) => Opacity = NextOpacity(Opacity, e.Delta);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OnCopy(this, e);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e) =>
        Run(() => ImageOutput.CopyToClipboard(_image, _settings.Current.CopyAsFile));

    private void OnSave(object sender, RoutedEventArgs e) =>
        Run(() => ImageOutput.Save(_image, this, _settings.Current));

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void Run(Action output)
    {
        try
        {
            output();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageBox.Show(this, ex.Message, "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static IntPtr IgnoreDpiChange(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
