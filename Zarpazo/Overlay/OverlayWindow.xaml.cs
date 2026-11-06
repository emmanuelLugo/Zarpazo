using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Zarpazo.Output;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Overlay;

public partial class OverlayWindow : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const int DwmwaTransitionsForceDisabled = 3;
    private const int DwmwaCloak = 13;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly BitmapSource _screenshot;
    private Point _dragStart;

    /// <param name="screenshot">The virtual screen, one image pixel per physical pixel.</param>
    /// <param name="bounds">The virtual screen in physical pixels; its origin can be negative.</param>
    public OverlayWindow(BitmapSource screenshot, Rectangle bounds)
    {
        InitializeComponent();
        _screenshot = screenshot;
        Screenshot.Source = screenshot;
        SelectionBorder.Data = Selection;
        SizeChanged += (_, e) => DimArea.Rect = new Rect(e.NewSize);

        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(IgnoreDpiChange);

        // WPF paints a blank frame when a window first shows, so keep it invisible until the screenshot is drawn.
        SetDwmFlag(hwnd, DwmwaTransitionsForceDisabled, true);
        SetDwmFlag(hwnd, DwmwaCloak, true);
        ContentRendered += (_, _) =>
        {
            SetDwmFlag(hwnd, DwmwaCloak, false);
            Activate();
        };

        // Physical pixels: WPF's Left/Top/Width/Height can't describe a window spanning monitors with different DPI.
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Enter || (ctrl && e.Key == Key.C))
        {
            Finish(ImageOutput.CopyToClipboard);
        }
        else if (ctrl && e.Key == Key.S)
        {
            Finish(image => ImageOutput.Save(image, this));
        }
    }

    /// <summary>Runs the output on the selected area and closes. Stays open if there is no selection, the user cancels or it fails.</summary>
    private void Finish(Func<BitmapSource, bool> output)
    {
        BitmapSource? crop = CropSelection();
        if (crop == null)
        {
            return;
        }

        try
        {
            if (!output(crop))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Close();
    }

    private BitmapSource? CropSelection()
    {
        Rect area = Selection.Rect;
        if (area.IsEmpty)
        {
            return null;
        }

        // Image pixels per layout unit; the window may be scaled for DPI, so don't assume 1.
        double scaleX = _screenshot.PixelWidth / ActualWidth;
        double scaleY = _screenshot.PixelHeight / ActualHeight;
        int x = Math.Clamp((int)Math.Round(area.X * scaleX), 0, _screenshot.PixelWidth - 1);
        int y = Math.Clamp((int)Math.Round(area.Y * scaleY), 0, _screenshot.PixelHeight - 1);
        int width = Math.Clamp((int)Math.Round(area.Width * scaleX), 0, _screenshot.PixelWidth - x);
        int height = Math.Clamp((int)Math.Round(area.Height * scaleY), 0, _screenshot.PixelHeight - y);
        if (width < 2 || height < 2)
        {
            return null; // a plain click, not a selection
        }

        var crop = new CroppedBitmap(_screenshot, new Int32Rect(x, y, width, height));
        crop.Freeze();
        return crop;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        Selection.Rect = new Rect(_dragStart, _dragStart);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            Selection.Rect = new Rect(_dragStart, e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    // Windows asks to rescale the window when its main monitor changes, which would break the 1:1 pixel fit.
    private static IntPtr IgnoreDpiChange(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void SetDwmFlag(IntPtr hwnd, int attribute, bool value)
    {
        int flag = value ? 1 : 0;
        DwmSetWindowAttribute(hwnd, attribute, ref flag, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
