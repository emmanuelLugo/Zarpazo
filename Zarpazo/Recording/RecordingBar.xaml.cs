using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>The small control that sits next to the recorded area: time and Stop while recording, then Copy and Save.</summary>
public partial class RecordingBar : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const int GapPixels = 10; // between the recorded area's frame and the bar

    private const double MeterWidth = 44;

    private readonly List<Border> _meterFills = [];
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

    /// <param name="meters">One level bar per sound source, so it is clear at a glance that each one is picking something up.</param>
    internal void ShowRecording(TimeSpan elapsed, TimeSpan limit, IReadOnlyList<AudioMeter> meters)
    {
        ElapsedText.Text = $"{Format(elapsed)} / {Format(limit)}";
        UpdateMeters(meters);
        ShowPanel(RecordingPanel);
    }

    private void UpdateMeters(IReadOnlyList<AudioMeter> meters)
    {
        if (_meterFills.Count != meters.Count)
        {
            MetersPanel.Children.Clear();
            _meterFills.Clear();
            foreach (AudioMeter meter in meters)
            {
                var fill = new Border { Width = 0, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)) };
                var track = new Border { Width = MeterWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Child = fill, ToolTip = meter.Name, VerticalAlignment = VerticalAlignment.Center };
                var icon = new TextBlock { Text = meter.IsMicrophone ? "\U0001F3A4" : "\U0001F50A", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = meter.Name };
                MetersPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), Children = { icon, track } });
                _meterFills.Add(fill);
            }
        }

        for (int i = 0; i < meters.Count; i++)
        {
            // Square root: quiet speech is far below full scale and would barely show on a straight scale.
            _meterFills[i].Width = MeterWidth * Math.Clamp(Math.Sqrt(meters[i].Level), 0, 1);
        }
    }

    internal void ShowEncoding(string message)
    {
        EncodingText.Text = message;
        ShowPanel(EncodingPanel);
    }

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
