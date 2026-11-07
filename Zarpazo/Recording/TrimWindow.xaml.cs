using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Zarpazo.Recording;

/// <summary>A small player to choose where a video starts and ends, and to save only that part as a new file.</summary>
public partial class TrimWindow : Window
{
    private readonly string _source;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _duration;
    private bool _updating;
    private bool _playing;
    private bool _ready; // the sliders fire their events while the XAML loads, before the rest of the controls exist

    internal TrimWindow(string source)
    {
        InitializeComponent();
        Theme.TitleBarTheme.Follow(this);
        _ready = true;
        _source = source;
        _timer.Tick += (_, _) => FollowPlayback();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Player.Close(); // lets go of the file
        };
        Player.Source = new Uri(source);
        Player.Play(); // a first frame only shows once it has started; it is paused as soon as it opens
    }

    /// <summary>The new video, once saved; null if the window was cancelled.</summary>
    internal string? TrimmedPath { get; private set; }

    private double Start => StartSlider.Value;

    private double End => EndSlider.Value;

    private void OnOpened(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        Player.Position = TimeSpan.Zero;
        _duration = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan.TotalSeconds : 0;

        StartSlider.Maximum = EndSlider.Maximum = PositionBar.Maximum = Math.Max(_duration, 0.1);
        EndSlider.Value = _duration;
        StartSlider.IsEnabled = EndSlider.IsEnabled = PlayButton.IsEnabled = SaveButton.IsEnabled = _duration > TrimRange.MinSeconds;
        Refresh();
    }

    private void OnFailed(object sender, ExceptionRoutedEventArgs e)
    {
        KeptText.Text = "No se pudo abrir el video para recortarlo.";
    }

    private void OnStartChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updating)
        {
            return;
        }
        Apply(movingStart: true);
        Seek(Start);
    }

    private void OnEndChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updating)
        {
            return;
        }
        Apply(movingStart: false);
        Seek(Math.Max(0, End - 0.05)); // the last picture that will be kept
    }

    // Keeps the two ends inside the video and apart, and shows the result.
    private void Apply(bool movingStart)
    {
        (double start, double end) = TrimRange.Clamp(Start, End, _duration, movingStart);
        _updating = true;
        StartSlider.Value = start;
        EndSlider.Value = end;
        _updating = false;
        Refresh();
    }

    private void Seek(double seconds)
    {
        StopPlayback();
        Player.Position = TimeSpan.FromSeconds(seconds);
        PositionBar.Value = seconds;
        PositionText.Text = Format(seconds);
    }

    private void Refresh()
    {
        StartText.Text = Format(Start);
        EndText.Text = Format(End);
        KeptText.Text = $"Se conservarán {Format(End - Start)} de {Format(_duration)}.";
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            StopPlayback();
            return;
        }

        if (Player.Position.TotalSeconds < Start || Player.Position.TotalSeconds >= End - 0.05)
        {
            Player.Position = TimeSpan.FromSeconds(Start); // play the part that will be kept, from its beginning
        }
        Player.Play();
        _playing = true;
        PlayButton.Content = "⏸ Pausa";
        _timer.Start();
    }

    private void FollowPlayback()
    {
        double position = Player.Position.TotalSeconds;
        PositionBar.Value = position;
        PositionText.Text = Format(position);
        if (position >= End)
        {
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        if (_playing)
        {
            Player.Pause();
            _playing = false;
        }
        _timer.Stop();
        PlayButton.Content = "▶ Reproducir";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        SetBusy(true);
        string target = Path.Combine(Path.GetDirectoryName(_source)!, Path.GetFileNameWithoutExtension(_source) + "_recorte.mp4");
        try
        {
            var progress = new Progress<double>(value => WorkBar.Value = value);
            await VideoTrimmer.TrimAsync(_source, target, TimeSpan.FromSeconds(Start), TimeSpan.FromSeconds(End), progress);
            TrimmedPath = target;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An async void handler: anything uncaught here would end the whole app.
            SetBusy(false);
            MessageBox.Show(this, $"No se pudo recortar el video: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetBusy(bool busy)
    {
        WorkBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WorkBar.Value = 0;
        SaveButton.IsEnabled = StartSlider.IsEnabled = EndSlider.IsEnabled = PlayButton.IsEnabled = !busy;
        KeptText.Text = busy ? "Recortando el video…" : KeptText.Text;
    }

    private static string Format(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss\.f");
}
