using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.Pin;

/// <summary>A capture left floating above everything else, where it was on the screen, to keep it in view while working.</summary>
public partial class PinnedWindow : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const double ShadowMarginDip = 14; // must match the Card's margin in the XAML
    private const double OpacityStep = 0.1;
    private const double MinOpacity = 0.2; // below this the picture can no longer be found on the screen
    private const int ToolsMinWidth = 180; // narrower than this, the buttons would cover the picture
    private static readonly TimeSpan ToastDuration = TimeSpan.FromMilliseconds(900);

    private readonly BitmapSource _image;
    private readonly SettingsStore _settings;
    private readonly IntPtr _hwnd;
    private double _scale = 1;
    private DispatcherTimer? _toastTimer;

    // State of a drag on the corner grip, in physical screen pixels.
    private POINT _gripStart;
    private (int Left, int Top, int Width, int Height) _gripStartRect;

    /// <param name="image">The capture, one image pixel per physical pixel.</param>
    /// <param name="left">Where its top-left corner goes, in physical screen pixels (can be negative).</param>
    internal PinnedWindow(BitmapSource image, int left, int top, SettingsStore settings)
    {
        InitializeComponent();
        _image = image;
        _settings = settings;
        Picture.Fill = new ImageBrush(image) { Stretch = Stretch.Fill };

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Moving it to a monitor with another scaling would make Windows resize it; it keeps its pixel size instead.
        HwndSource.FromHwnd(_hwnd).AddHook(IgnoreDpiChange);
        Place(left, top, 1);

        MouseEnter += (_, _) => ShowControls(true);
        MouseLeave += (_, _) => ShowControls(false);
    }

    /// <summary>The opacity after one step of the mouse wheel (up makes it more solid), kept within a usable range.</summary>
    internal static double NextOpacity(double current, int wheelDelta) =>
        Math.Clamp(Math.Round(current + (wheelDelta > 0 ? OpacityStep : -OpacityStep), 1), MinOpacity, 1.0);

    private int ShadowMargin => (int)Math.Round(ShadowMarginDip * VisualTreeHelper.GetDpi(this).DpiScaleX);

    // WPF's Left/Top/Width/Height can't place a window across monitors with different DPI, so this works in physical pixels
    // like the overlay. The window is the picture plus the room for its shadow, so the picture lands exactly on `left`, `top`.
    private void Place(int left, int top, double scale)
    {
        _scale = scale;
        (int width, int height) = ScaledSize(scale);
        int margin = ShadowMargin;
        SetWindowPos(_hwnd, IntPtr.Zero, left - margin, top - margin, width + 2 * margin, height + 2 * margin, SwpNoZOrder | SwpNoActivate);
        // Nearest neighbour keeps the pixels crisp at 100 %; any other size needs smoothing.
        RenderOptions.SetBitmapScalingMode(Picture, scale == 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
    }

    private (int Width, int Height) ScaledSize(double scale) =>
        ((int)Math.Round(_image.PixelWidth * scale), (int)Math.Round(_image.PixelHeight * scale));

    /// <summary>Where the picture is on the screen now (the window may have been dragged), in physical pixels.</summary>
    private (int Left, int Top, int Width, int Height) PictureRect()
    {
        GetWindowRect(_hwnd, out RECT window);
        int margin = ShadowMargin;
        (int width, int height) = ScaledSize(_scale);
        return (window.Left + margin, window.Top + margin, width, height);
    }

    /// <summary>Scales the picture so the point at the given fractions of it stays under the given screen point.</summary>
    private void ScaleTo(double scale, int anchorX, int anchorY, double fractionX, double fractionY)
    {
        scale = PinScale.Clamp(scale, _image.PixelWidth, _image.PixelHeight);
        (int width, int height) = ScaledSize(scale);
        (int left, int top) = PinScale.Origin(anchorX, anchorY, fractionX, fractionY, width, height);
        Place(left, top, scale);
        ShowControls(IsMouseOver); // the toolbar only fits when the picture is wide enough
    }

    private void ActualSize()
    {
        (int left, int top, int width, int height) = PictureRect();
        ScaleTo(1, left + width / 2, top + height / 2, 0.5, 0.5);
        Notify("Tamaño original");
    }

    private void ShowControls(bool hovering)
    {
        CloseButton.Visibility = hovering ? Visibility.Visible : Visibility.Collapsed;
        Grip.Visibility = CloseButton.Visibility;
        Tools.Visibility = hovering && ScaledSize(_scale).Width >= ToolsMinWidth ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A short message in the middle of the picture, for the gestures that have no button.</summary>
    private void Notify(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };
        _toastTimer.Start();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ActualSize();
        }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zoom around the cursor, so what is being looked at stays put.
            GetCursorPos(out POINT cursor);
            (int left, int top, int width, int height) = PictureRect();
            double fractionX = Math.Clamp((cursor.X - left) / (double)width, 0, 1);
            double fractionY = Math.Clamp((cursor.Y - top) / (double)height, 0, 1);
            ScaleTo(PinScale.Next(_scale, e.Delta, _image.PixelWidth, _image.PixelHeight), cursor.X, cursor.Y, fractionX, fractionY);
            Notify($"Tamaño {Math.Round(_scale * 100)} %");
        }
        else
        {
            Opacity = NextOpacity(Opacity, e.Delta);
            Notify($"Opacidad {Math.Round(Opacity * 100)} %");
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (ctrl && e.Key == Key.C)
        {
            OnCopy(this, e);
        }
        else if (ctrl && e.Key is Key.D0 or Key.NumPad0)
        {
            ActualSize();
        }
    }

    // The corner grip is dragged with the mouse captured and measured on the screen: the window itself moves with the
    // drag, so deltas relative to the grip would feed back into themselves.
    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        GetCursorPos(out _gripStart);
        _gripStartRect = PictureRect();
        Grip.CaptureMouse();
        e.Handled = true; // not a drag of the whole window
    }

    private void OnGripMove(object sender, MouseEventArgs e)
    {
        if (!Grip.IsMouseCaptured)
        {
            return;
        }

        GetCursorPos(out POINT cursor);
        (int left, int top, int width, int height) = _gripStartRect;
        // Follow the corner along both axes; the average keeps the proportions whichever way it is pulled.
        double scale = ((width + cursor.X - _gripStart.X) / (double)_image.PixelWidth
            + (height + cursor.Y - _gripStart.Y) / (double)_image.PixelHeight) / 2;
        ScaleTo(scale, left, top, 0, 0);
    }

    private void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        Grip.ReleaseMouseCapture();
        Notify($"Tamaño {Math.Round(_scale * 100)} %");
        e.Handled = true;
    }

    private void OnCopy(object sender, RoutedEventArgs e) =>
        Run(() => ImageOutput.CopyToClipboard(_image, _settings.Current.CopyAsFile));

    private void OnSave(object sender, RoutedEventArgs e) =>
        Run(() => ImageOutput.Save(_image, this, _settings.Current));

    private void OnActualSize(object sender, RoutedEventArgs e) => ActualSize();

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
