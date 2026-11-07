using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>The small control that sits next to the recorded area: time and Stop while recording, then Copy and Save.</summary>
public partial class RecordingBar : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const int GapPixels = 10; // between the recorded area's frame and the bar

    private Rectangle _region;

    public RecordingBar()
    {
        InitializeComponent();
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(IgnoreDpiChange);
        // Only matters when the bar has to sit over the recorded area (a full-screen recording); otherwise it is outside it.
        WindowNative.ExcludeFromScreenCapture(hwnd);
    }

    internal event EventHandler? StopRequested;
    internal event EventHandler? CancelRequested;
    internal event EventHandler? CopyRequested;
    internal event EventHandler? SaveRequested;
    internal event EventHandler? CloseRequested;

    internal void ShowRecording(TimeSpan elapsed, TimeSpan limit)
    {
        ElapsedText.Text = $"{Format(elapsed)} / {Format(limit)}";
        ShowPanel(RecordingPanel);
    }

    internal void ShowEncoding() => ShowPanel(EncodingPanel);

    internal void ShowDone(string message)
    {
        DoneText.Text = message;
        ShowPanel(DonePanel);
    }

    internal void ShowFailure(string message)
    {
        FailureText.Text = message;
        ShowPanel(FailurePanel);
    }

    private static string Format(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";

    private void ShowPanel(UIElement panel)
    {
        foreach (UIElement other in new UIElement[] { RecordingPanel, EncodingPanel, DonePanel, FailurePanel })
        {
            other.Visibility = other == panel ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_region != Rectangle.Empty)
        {
            Reposition(); // each state has its own width
        }
    }

    /// <summary>Puts the bar under the recorded area, above it if there is no room, or inside it as a last resort.</summary>
    internal void PlaceNear(Rectangle region)
    {
        _region = region;
        Reposition();
    }

    private void Reposition()
    {
        Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = (int)Math.Ceiling(DesiredSize.Width * scale);
        int height = (int)Math.Ceiling(DesiredSize.Height * scale);

        Rectangle screen = Screen.FromRectangle(_region).Bounds;
        int left = Math.Clamp(_region.Left + (_region.Width - width) / 2, screen.Left, Math.Max(screen.Left, screen.Right - width));
        int below = _region.Bottom + GapPixels;
        int above = _region.Top - GapPixels - height;
        int top = below + height <= screen.Bottom ? below
            : above >= screen.Top ? above
            : Math.Max(screen.Top, _region.Bottom - height - GapPixels); // the area fills the screen: inside it, kept out of the picture by Windows

        WindowNative.Place(new WindowInteropHelper(this).Handle, new Rectangle(left, top, width, height));
    }

    private void OnStop(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopy(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnSave(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private static IntPtr IgnoreDpiChange(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }
}
