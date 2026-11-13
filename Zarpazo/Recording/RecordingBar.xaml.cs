using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Cursors = System.Windows.Input.Cursors;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Path = System.Windows.Shapes.Path;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>
/// The floating pill that sits next to the recorded area: time and Stop while recording, then Copy and Save. It can be
/// dragged away by its dots; from then on it stays where it was put.
/// </summary>
public partial class RecordingBar : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const int GapPixels = 10; // between the recorded area's frame and the pill

    private const double MeterWidth = 56;

    // The icons the bar swaps in code, on the same 24 x 24 grid as the ones in the XAML.
    internal static readonly Geometry PauseGlyph = Frozen("M6,4 H9.5 A1,1 0 0 1 10.5,5 V19 A1,1 0 0 1 9.5,20 H6 A1,1 0 0 1 5,19 V5 A1,1 0 0 1 6,4 Z M14.5,4 H18 A1,1 0 0 1 19,5 V19 A1,1 0 0 1 18,20 H14.5 A1,1 0 0 1 13.5,19 V5 A1,1 0 0 1 14.5,4 Z");
    internal static readonly Geometry ResumeGlyph = Frozen("M7,4.5 A1,1 0 0 1 8.5,3.6 L19.5,11.1 A1,1 0 0 1 19.5,12.9 L8.5,20.4 A1,1 0 0 1 7,19.5 Z");
    internal static readonly Geometry MicrophoneGlyph = Frozen("M12,2 A3,3 0 0 0 9,5 V12 A3,3 0 0 0 15,12 V5 A3,3 0 0 0 12,2 Z M19,10 V12 A7,7 0 0 1 5,12 V10 M12,19 V22");
    internal static readonly Geometry MicrophoneOffGlyph = Frozen("M12,2 A3,3 0 0 0 9,5 V12 A3,3 0 0 0 15,12 V5 A3,3 0 0 0 12,2 Z M19,10 V12 A7,7 0 0 1 5,12 V10 M12,19 V22 M3,3 L21,21");
    internal static readonly Geometry SpeakerGlyph = Frozen("M11,5 L6,9 H3 V15 H6 L11,19 Z M15.5,8.5 A5,5 0 0 1 15.5,15.5 M18.5,5.5 A9,9 0 0 1 18.5,18.5");
    internal static readonly Geometry SpeakerOffGlyph = Frozen("M11,5 L6,9 H3 V15 H6 L11,19 Z M22,9 L16,15 M16,9 L22,15");

    private static readonly Brush RecordingRed = Frozen(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush PausedAmber = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));

    private const float GainStep = 0.1f;

    private readonly List<Border> _meterFills = [];
    private readonly List<(Path Icon, StackPanel Group, AudioMeter Meter)> _meterIcons = [];
    private readonly List<float> _gains = [];
    private readonly List<float> _gainBeforeMute = [];
    private Rectangle _region;
    private bool _movedByHand;
    private bool? _blinking;

    public RecordingBar()
    {
        InitializeComponent();
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource source = HwndSource.FromHwnd(hwnd);
        source.AddHook(IgnoreDpiChange);
        // See-through around the pill (its round corners and soft shadow) without making it a layered window: Windows
        // refuses to keep a layered window out of screen copies, and in a full-screen recording the bar is over the picture.
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        WindowNative.SeeThroughBackground(hwnd);
        // Only matters when the bar has to sit over the recorded area (a full-screen recording); otherwise it is outside it.
        WindowNative.ExcludeFromScreenCapture(hwnd);
    }

    /// <summary>A source's volume was changed (0 is muted): its position in the list of meters, and the new volume.</summary>
    internal event Action<int, float>? GainChanged;

    internal event EventHandler? PauseRequested;
    internal event EventHandler? StopRequested;
    internal event EventHandler? CancelRequested;
    internal event EventHandler? CopyRequested;
    internal event EventHandler? SaveRequested;
    internal event EventHandler? CloseRequested;

    /// <param name="meters">One level bar per sound source, so it is clear at a glance that each one is picking something up.</param>
    internal void ShowRecording(TimeSpan elapsed, TimeSpan limit, IReadOnlyList<AudioMeter> meters, bool paused = false)
    {
        ElapsedText.Text = Format(elapsed);
        LimitText.Text = $"/ {Format(limit)}";
        // The red dot blinks while recording; while paused it turns amber and stays still, and the button offers to carry on.
        Brush status = paused ? PausedAmber : RecordingRed;
        StatusDot.Fill = status;
        StatusText.Foreground = status;
        StatusText.Text = paused ? "PAUSA" : "REC";
        Blink(!paused);
        PauseIcon.Data = paused ? ResumeGlyph : PauseGlyph;
        UpdateMeters(meters);
        ShowPanel(RecordingPanel);
    }

    /// <summary>The seconds left before the recording starts.</summary>
    internal void ShowCountdown(int secondsLeft)
    {
        CountdownNumber.Text = secondsLeft.ToString();
        ShowPanel(CountdownPanel);
    }

    private void UpdateMeters(IReadOnlyList<AudioMeter> meters)
    {
        if (_meterFills.Count != meters.Count)
        {
            BuildMeters(meters);
        }
        MetersDivider.Visibility = meters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
            var fill = new Border { Width = 0, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = AudioLevelDisplay.Color(0) };
            var track = new Border { Width = MeterWidth, Height = 5, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), Child = fill, VerticalAlignment = VerticalAlignment.Center };
            var icon = new Path { Style = (Style)FindResource("Glyph"), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), Background = Brushes.Transparent, Children = { icon, track } };
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
        (Path icon, StackPanel group, AudioMeter meter) = _meterIcons[source];
        float gain = _gains[source];
        icon.Data = meter.IsMicrophone ? gain == 0 ? MicrophoneOffGlyph : MicrophoneGlyph : gain == 0 ? SpeakerOffGlyph : SpeakerGlyph;
        string state = gain == 0 ? "silenciado" : $"{gain:P0}";
        string hint = $"{meter.Name} · {state}\nClic: silenciar · Rueda: volumen";
        icon.ToolTip = hint;
        group.ToolTip = hint;
        group.Opacity = gain == 0 ? 0.5 : 1;
    }

    // A slow fade in and out, the usual sign that something is being recorded; it goes still while paused.
    private void Blink(bool on)
    {
        if (_blinking == on)
        {
            return;
        }

        _blinking = on;
        StatusDot.BeginAnimation(OpacityProperty, on
            ? new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() }
            : null);
    }

    /// <param name="progress">How far along, 0 to 1; null when it can't be told.</param>
    internal void ShowEncoding(string message, double? progress = null)
    {
        bool same = EncodingPanel.Visibility == Visibility.Visible && EncodingText.Text == message;
        EncodingText.Text = message;
        EncodingProgress.IsIndeterminate = progress == null;
        EncodingProgress.Value = (progress ?? 0) * 100;
        if (same)
        {
            return; // only the bar moved: the pill keeps its size and place
        }
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
        if (panel != RecordingPanel)
        {
            Blink(false);
        }
        foreach (UIElement other in new UIElement[] { RecordingPanel, CountdownPanel, EncodingPanel, DonePanel, FailurePanel })
        {
            other.Visibility = other == panel ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_region != Rectangle.Empty && !_movedByHand)
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

        // The window is bigger than the pill by the room its shadow takes; the gap and the screen edges count from the pill.
        int shadow = (int)Math.Round(Pill.Margin.Left * scale);
        Rectangle screen = Screen.FromRectangle(_region).Bounds;
        int left = Math.Clamp(_region.Left + (_region.Width - width) / 2, screen.Left - shadow, Math.Max(screen.Left - shadow, screen.Right - width + shadow));
        int below = _region.Bottom + GapPixels - shadow;
        int above = _region.Top - GapPixels - height + shadow;
        int top = below + height - shadow <= screen.Bottom ? below
            : above + shadow >= screen.Top ? above
            : Math.Max(screen.Top - shadow, _region.Bottom - height + shadow - GapPixels); // the area fills the screen: inside it, kept out of the picture by Windows

        WindowNative.Place(new WindowInteropHelper(this).Handle, new Rectangle(left, top, width, height));
    }

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        _movedByHand = true; // put there on purpose: changing state no longer brings it back next to the area
        e.Handled = true;
    }

    private static Geometry Frozen(string path)
    {
        Geometry geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnPause(object sender, RoutedEventArgs e) => PauseRequested?.Invoke(this, EventArgs.Empty);

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
