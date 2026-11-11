using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Cursors = System.Windows.Input.Cursors;
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

    private const double MeterWidth = 64;

    private const float GainStep = 0.1f;

    private readonly List<Border> _meterFills = [];
    private readonly List<(TextBlock Icon, StackPanel Group, AudioMeter Meter)> _meterIcons = [];
    private readonly List<float> _gains = [];
    private readonly List<float> _gainBeforeMute = [];
    private Rectangle _region;

    public RecordingBar()
    {
        InitializeComponent();
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(IgnoreDpiChange);
        // Only matters when the bar has to sit over the recorded area (a full-screen recording); otherwise it is outside it.
        WindowNative.ExcludeFromScreenCapture(hwnd);
    }

    /// <summary>A source's volume was changed (0 is muted): its position in the list of meters, and the new volume.</summary>
    internal event Action<int, float>? GainChanged;

    internal event EventHandler? PauseRequested;
    internal event EventHandler? StopRequested;
    internal event EventHandler? CancelRequested;
    internal event EventHandler? EditRequested;
    internal event EventHandler? CopyRequested;
    internal event EventHandler? SaveRequested;
    internal event EventHandler? CloseRequested;

    /// <param name="meters">One level bar per sound source, so it is clear at a glance that each one is picking something up.</param>
    internal void ShowRecording(TimeSpan elapsed, TimeSpan limit, IReadOnlyList<AudioMeter> meters, bool paused = false)
    {
        ElapsedText.Text = paused ? $"{Format(elapsed)} · en pausa" : $"{Format(elapsed)} / {Format(limit)}";
        // The red dot means "recording"; while paused it turns amber, and the button offers to carry on.
        StatusDot.Fill = new SolidColorBrush(paused ? Color.FromRgb(0xFD, 0xD8, 0x35) : Color.FromRgb(0xE5, 0x39, 0x35));
        PauseText.Text = paused ? "Reanudar" : "Pausa";
        PauseIcon.Data = Geometry.Parse(paused ? "M0,0 L9,5 L0,10 Z" : "M1,0 H3.5 V10 H1 Z M6.5,0 H9 V10 H6.5 Z");
        UpdateMeters(meters);
        ShowPanel(RecordingPanel);
    }

    /// <summary>The seconds left before the recording starts.</summary>
    internal void ShowCountdown(int secondsLeft)
    {
        CountdownText.Text = $"Empieza en {secondsLeft}…";
        ShowPanel(CountdownPanel);
    }

    private void UpdateMeters(IReadOnlyList<AudioMeter> meters)
    {
        if (_meterFills.Count != meters.Count)
        {
            BuildMeters(meters);
        }

        for (int i = 0; i < meters.Count; i++)
        {
            double fraction = AudioLevelDisplay.Fraction(meters[i].Level);
            _meterFills[i].Width = MeterWidth * fraction;
            _meterFills[i].Background = AudioLevelDisplay.Color(fraction);
        }
    }

    // One icon and one bar per source. Clicking the icon mutes it; the wheel over it raises or lowers its volume.
    private void BuildMeters(IReadOnlyList<AudioMeter> meters)
    {
        MetersPanel.Children.Clear();
        _meterFills.Clear();
        _meterIcons.Clear();
        _gains.Clear();
        _gainBeforeMute.Clear();

        for (int i = 0; i < meters.Count; i++)
        {
            int source = i;
            AudioMeter meter = meters[i];
            var fill = new Border { Width = 0, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = AudioLevelDisplay.Color(0) };
            var track = new Border { Width = MeterWidth, Height = 8, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Child = fill, VerticalAlignment = VerticalAlignment.Center };
            var icon = new TextBlock { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), Background = Brushes.Transparent, Children = { icon, track } };
            icon.MouseLeftButtonDown += (_, e) =>
            {
                ToggleMute(source);
                e.Handled = true;
            };
            group.MouseWheel += (_, e) =>
            {
                SetGain(source, _gains[source] + (e.Delta > 0 ? GainStep : -GainStep));
                e.Handled = true;
            };

            MetersPanel.Children.Add(group);
            _meterFills.Add(fill);
            _meterIcons.Add((icon, group, meter));
            _gains.Add(meter.Gain);
            _gainBeforeMute.Add(meter.Gain > 0 ? meter.Gain : 1f);
            ShowSource(source);
        }
    }

    private void ToggleMute(int source)
    {
        if (_gains[source] > 0)
        {
            _gainBeforeMute[source] = _gains[source];
            SetGain(source, 0);
        }
        else
        {
            SetGain(source, _gainBeforeMute[source]);
        }
    }

    private void SetGain(int source, float gain)
    {
        gain = (float)Math.Clamp(Math.Round(gain * 10) / 10, 0, AudioMixer.MaxGain); // steps of 10 %, so the label stays tidy
        if (gain == _gains[source])
        {
            return;
        }

        _gains[source] = gain;
        ShowSource(source);
        GainChanged?.Invoke(source, gain);
    }

    // The icon, the hint and the dimming say whether a source is muted, and how loud it is otherwise.
    private void ShowSource(int source)
    {
        (TextBlock icon, StackPanel group, AudioMeter meter) = _meterIcons[source];
        float gain = _gains[source];
        icon.Text = gain == 0 ? "\U0001F507" : meter.IsMicrophone ? "\U0001F3A4" : "\U0001F50A";
        string state = gain == 0 ? "silenciado" : $"{gain:P0}";
        string hint = $"{meter.Name} · {state}\nClic: silenciar · Rueda: volumen";
        icon.ToolTip = hint;
        group.ToolTip = hint;
        group.Opacity = gain == 0 ? 0.5 : 1;
    }

    internal void ShowEncoding(string message)
    {
        EncodingText.Text = message;
        ShowPanel(EncodingPanel);
    }

    /// <param name="canEdit">Offers to open the video editor (videos only).</param>
    internal void ShowDone(string message, bool canEdit = false)
    {
        EditButton.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
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
        foreach (UIElement other in new UIElement[] { RecordingPanel, CountdownPanel, EncodingPanel, DonePanel, FailurePanel })
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

    private void OnPause(object sender, RoutedEventArgs e) => PauseRequested?.Invoke(this, EventArgs.Empty);

    private void OnStop(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnEdit(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this, EventArgs.Empty);

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
