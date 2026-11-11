using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Zarpazo.Recording;

namespace Zarpazo.Shell;

/// <summary>
/// The notice that a new version is downloaded and ready: a card in the bottom-right corner, over the taskbar's work area,
/// with the choice to restart now or install when Zarpazo closes. It never takes the focus and stays out of screenshots and
/// recordings. Closes itself after a while; the tray menu keeps the entry.
/// </summary>
public partial class UpdateToast : Window
{
    private static readonly TimeSpan StaysFor = TimeSpan.FromSeconds(30);
    private const double EdgeGap = 8; // from the edge of the work area, on top of the card's own shadow margin

    private readonly Action _restartNow;
    private readonly Action _onClose;
    private readonly Action _notes;
    private readonly DispatcherTimer _timer = new() { Interval = StaysFor };
    private bool _closing;

    /// <param name="version">The new version, as shown.</param>
    /// <param name="restartNow">The person chose to restart and install right away.</param>
    /// <param name="onClose">The person chose to install when Zarpazo closes.</param>
    /// <param name="notes">The person asked to read what changed.</param>
    public UpdateToast(string version, Action restartNow, Action onClose, Action notes)
    {
        InitializeComponent();
        _restartNow = restartNow;
        _onClose = onClose;
        _notes = notes;

        Heading.Text = $"Zarpazo {version} está lista";
        Detail.Text = "Ya se descargó y se comprobó. Al reiniciar se instala sola y Zarpazo vuelve a abrirse.";

        SourceInitialized += (_, _) => WindowNative.ExcludeFromScreenCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => PlaceAndShow();
        MouseEnter += (_, _) => _timer.Stop(); // reading it or about to click: don't take it away
        MouseLeave += (_, _) => RestartTimer();
        _timer.Tick += (_, _) => Dismiss();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Closes the card with a short fade.</summary>
    public void Dismiss()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => Close();
        Card.BeginAnimation(OpacityProperty, fade);
    }

    private void PlaceAndShow()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - EdgeGap;
        Top = area.Bottom - ActualHeight - EdgeGap;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        RestartTimer();
    }

    private void RestartTimer()
    {
        if (!_closing)
        {
            _timer.Stop();
            _timer.Start();
        }
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        Dismiss();
        _restartNow();
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        Dismiss();
        _onClose();
    }

    private void OnNotes(object sender, RoutedEventArgs e) => _notes();

    private void OnDismiss(object sender, RoutedEventArgs e) => Dismiss();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Dismiss();
        }
    }
}
