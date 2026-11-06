using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Editor;
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

    private const double ToolbarGap = 6;

    private readonly BitmapSource _screenshot;
    private Point _dragStart;
    private Annotation? _drawing;
    private TextBox? _textBox;
    private Point _textOrigin;
    private Color _textColor;
    private double _textThickness;

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
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Enter || (ctrl && e.Key == Key.C))
        {
            OnCopy(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.S)
        {
            OnSave(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.Z)
        {
            OnUndo(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.Y)
        {
            OnRedo(this, EventArgs.Empty);
        }
    }

    private void OnCopy(object? sender, EventArgs e) => Finish(ImageOutput.CopyToClipboard);

    private void OnSave(object? sender, EventArgs e) => Finish(image => ImageOutput.Save(image, this));

    private void OnUndo(object? sender, EventArgs e) => Annotations.Undo();

    private void OnRedo(object? sender, EventArgs e) => Annotations.Redo();

    /// <summary>Runs the output on the selected area and closes. Stays open if there is no selection, the user cancels or it fails.</summary>
    private void Finish(Func<BitmapSource, bool> output)
    {
        CommitText(); // text still being typed belongs in the result
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
        if (!HasSelection)
        {
            return null;
        }

        Rect area = Selection.Rect;
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
        return Annotations.Compose(crop, x, y, scaleX, scaleY);
    }

    // A click without dragging leaves a zero-size rectangle, which is not a selection.
    private bool HasSelection => Selection.Rect is { IsEmpty: false, Width: >= 2, Height: >= 2 };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_textBox is { IsMouseOver: true })
        {
            return; // clicking inside the text being typed
        }
        CommitText();

        Point point = e.GetPosition(this);
        if (Toolbar.Tool == AnnotationTool.Select)
        {
            _dragStart = point;
            Annotations.Clear();
            Toolbar.Visibility = Visibility.Collapsed;
            Selection.Rect = new Rect(point, point);
        }
        else if (HasSelection && Selection.Rect.Contains(point))
        {
            if (Toolbar.Tool == AnnotationTool.Text)
            {
                BeginText(point);
                return;
            }
            _drawing = Annotation.Create(Toolbar.Tool, Toolbar.Color, Toolbar.Thickness, point);
            Annotations.Begin(_drawing);
        }
        else
        {
            return; // drawing tools ignore clicks outside the capture area
        }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        Point point = e.GetPosition(this);
        if (_drawing != null)
        {
            Rect area = Selection.Rect;
            _drawing.Extend(new Point(Math.Clamp(point.X, area.Left, area.Right), Math.Clamp(point.Y, area.Top, area.Bottom)));
            Annotations.InvalidateVisual();
        }
        else
        {
            Selection.Rect = new Rect(_dragStart, point);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }
        ReleaseMouseCapture();

        if (_drawing != null)
        {
            Annotations.Commit(_drawing);
            _drawing = null;
        }
        else if (HasSelection)
        {
            Annotations.ClipArea = Selection.Rect;
            ShowToolbar();
        }
    }

    private void BeginText(Point point)
    {
        var brush = new SolidColorBrush(Toolbar.Color);
        _textColor = Toolbar.Color;
        _textThickness = Toolbar.Thickness;
        _textOrigin = point;
        _textBox = new TextBox
        {
            Foreground = brush,
            CaretBrush = brush,
            FontFamily = TextAnnotation.Font,
            FontWeight = TextAnnotation.Weight,
            FontSize = TextAnnotation.SizeFor(_textThickness),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 60,
            AcceptsReturn = true,
            Cursor = Cursors.IBeam,
        };
        _textBox.PreviewKeyDown += OnTextKeyDown;
        _textBox.Loaded += (_, _) => _textBox?.Focus(); // can't take focus before it has been laid out
        // The 1px border sits outside the text, so shift the box to keep the text exactly where it will be drawn.
        Canvas.SetLeft(_textBox, point.X - 1);
        Canvas.SetTop(_textBox, point.Y - 1);
        UiLayer.Children.Add(_textBox);
    }

    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RemoveTextBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitText();
            e.Handled = true;
        }
    }

    private void CommitText()
    {
        if (_textBox == null)
        {
            return;
        }

        string text = _textBox.Text.TrimEnd();
        RemoveTextBox();
        Annotations.Commit(new TextAnnotation(_textColor, _textThickness, _textOrigin, text));
    }

    private void RemoveTextBox()
    {
        UiLayer.Children.Remove(_textBox);
        _textBox = null;
        Focus(); // otherwise Esc and the shortcuts stop reaching the window
    }

    private void ShowToolbar()
    {
        Rect area = Selection.Rect;
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = Toolbar.DesiredSize;

        // Under the selection, right-aligned; above it if there is no room; inside it if the selection fills the screen.
        double left = Math.Clamp(area.Right - size.Width, 0, Math.Max(0, ActualWidth - size.Width));
        double top = area.Bottom + ToolbarGap;
        if (top + size.Height > ActualHeight)
        {
            top = area.Top - size.Height - ToolbarGap;
        }
        if (top < 0)
        {
            top = Math.Max(0, area.Bottom - size.Height - ToolbarGap);
        }
        Canvas.SetLeft(Toolbar, left);
        Canvas.SetTop(Toolbar, top);
    }

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
