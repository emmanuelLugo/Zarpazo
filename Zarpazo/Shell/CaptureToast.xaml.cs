using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Zarpazo.Recording;

namespace Zarpazo.Shell;

/// <summary>
/// A short notice in the bottom-right corner that a capture was copied or saved. It makes no sound, never takes the focus
/// (the paste goes where the person was), stays out of the next capture and closes by itself; a click closes it sooner.
/// </summary>
public partial class CaptureToast : Window
{
    private static readonly TimeSpan StaysFor = TimeSpan.FromSeconds(2.5);
    private const double EdgeGap = 8; // from the edge of the work area, on top of the card's own shadow margin

    private readonly DispatcherTimer _timer = new() { Interval = StaysFor };

    /// <param name="heading">What happened.</param>
    /// <param name="path">The file written, if one was: its name and folder are shown.</param>
    public CaptureToast(string heading, string? path = null)
    {
        InitializeComponent();
        Heading.Text = heading;
        // The name first: it is what tells one capture from another when the line has to be cut.
        Detail.Text = path == null ? "" : $"{Path.GetFileName(path)} · {Path.GetDirectoryName(path)}";
        Detail.ToolTip = path;
        Detail.Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;

        SourceInitialized += (_, _) => WindowNative.ExcludeFromScreenCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - EdgeGap;
            Top = area.Bottom - ActualHeight - EdgeGap;
            Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            _timer.Start();
        };
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => Close();
}
