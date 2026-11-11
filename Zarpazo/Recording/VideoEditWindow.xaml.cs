using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Zarpazo.Recording;

/// <summary>
/// A small video editor for a recording: cut it in pieces, delete the ones that are not wanted, change their order, repeat
/// or mute them, adjust where each one starts and ends, and save the result as a new video.
/// </summary>
public partial class VideoEditWindow : Window
{
    private const double FrameSeconds = 1.0 / 30;

    private readonly string _source;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private EditTimeline? _timeline;
    private double _playhead; // in seconds of the edited video
    private double _zoom = 1;
    private bool _ready; // sliders fire their events while the XAML loads, before the rest of the controls exist
    private bool _playing;
    private int _playIndex; // the piece being played
    private long _settleUntil; // right after a jump the player still reports the old position for a moment
    private long? _tailSince; // see PlayerPosition
    private bool _opening;
    private bool _busy;
    private bool _saved;
    private bool _closed;

    internal VideoEditWindow(string source)
    {
        InitializeComponent();
        Theme.TitleBarTheme.Follow(this);
        _ready = true;
        _source = source;
        _timer.Tick += (_, _) => FollowPlayback();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            Player.Close(); // lets go of the file
        };
        Player.Source = new Uri(source);
        Player.Play(); // a first picture only shows once it has started; it is paused as soon as it opens
    }

    /// <summary>The new video, once saved; null if the window was closed without saving.</summary>
    internal string? EditedPath { get; private set; }

    /// <summary>The edit, once the video has opened.</summary>
    internal EditTimeline? Timeline => _timeline;

    private bool CanEdit => _timeline != null && !_busy;

    // ---------- opening

    // An async void event handler: anything uncaught here would end the whole app.
    private async void OnOpened(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        Player.Position = TimeSpan.Zero;
        if (_timeline != null || _opening)
        {
            return;
        }

        _opening = true;
        try
        {
            // The player's own NaturalDuration drops the fractional second (13.6 s reads as 13), which would cut the end off
            // every video. The editing engine that writes the result knows the true length.
            MediaClip clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(_source));
            double duration = clip.OriginalDuration.TotalSeconds;
            if (_closed)
            {
                return;
            }
            if (duration < EditTimeline.MinSeconds * 2)
            {
                KeptText.Text = "El video es demasiado corto para editarlo.";
                return;
            }

            _timeline = new EditTimeline(duration);
            _timeline.Changed += OnTimelineChanged;
            View.Timeline = _timeline;
            View.SelectedIndex = 0;
            SetPlayhead(0);
            UpdateScale();
            RefreshState();
            await LoadThumbnailsAsync(clip);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            KeptText.Text = "No se pudo abrir el video para editarlo.";
        }
    }

    private void OnFailed(object sender, ExceptionRoutedEventArgs e)
    {
        KeptText.Text = "No se pudo abrir el video para editarlo.";
    }

    // Pictures for the blocks, evenly over the video. They arrive one by one and the blocks fill in as they do.
    private async Task LoadThumbnailsAsync(MediaClip clip)
    {
        try
        {
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var properties = clip.GetVideoEncodingProperties();
            uint width = 192;
            uint height = Math.Max(1u, (uint)Math.Round(width * (double)properties.Height / Math.Max(1u, properties.Width)));
            double length = clip.OriginalDuration.TotalSeconds;
            int count = Math.Clamp((int)Math.Ceiling(length / 2), 12, 48);
            var pictures = new BitmapSource?[count];
            View.SetThumbnails(pictures);

            for (int i = 0; i < count && !_closed; i++)
            {
                var at = TimeSpan.FromSeconds(Math.Max(0, Math.Min(length - 0.05, (i + 0.5) * length / count)));
                using IRandomAccessStream stream = await composition.GetThumbnailAsync(at, (int)width, (int)height, VideoFramePrecision.NearestFrame);
                var decoder = BitmapDecoder.Create(stream.AsStreamForRead(), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource picture = decoder.Frames[0];
                picture.Freeze();
                pictures[i] = picture;
                View.InvalidateVisual();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Without pictures the blocks are plain, which still edits perfectly well.
        }
    }

    // ---------- the timeline

    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        StopPlayback();
        View.SelectedIndex = Math.Clamp(View.SelectedIndex, 0, _timeline!.Segments.Count - 1);
        _playhead = Math.Clamp(_playhead, 0, _timeline.Duration);
        View.Playhead = _playhead;
        if (!View.IsDragging)
        {
            UpdateScale();
            ShowFrame();
        }
        RefreshState();
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => RefreshState();

    private void OnScrubbed(object? sender, double time)
    {
        StopPlayback();
        SetPlayhead(time);
    }

    // While an edge is dragged the player shows the picture at that edge.
    private void OnPreviewed(object? sender, double sourceTime)
    {
        StopPlayback();
        Player.Position = TimeSpan.FromSeconds(sourceTime);
    }

    private void OnGestureFinished(object? sender, EventArgs e)
    {
        UpdateScale();
        ShowFrame();
        RefreshState();
    }

    // The whole edit fits the width at zoom 1; more than that and the timeline scrolls.
    private void UpdateScale()
    {
        if (_timeline == null || View.IsDragging)
        {
            return;
        }

        double viewport = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth;
        if (viewport <= 0)
        {
            return;
        }

        View.MinWidth = viewport;
        View.PixelsPerSecond = (viewport - TimelineLayout.Padding * 2 - 1) / Math.Max(0.1, _timeline.Duration) * _zoom;
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateScale();

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready)
        {
            return;
        }
        _zoom = e.NewValue;
        UpdateScale();
    }

    // Ctrl + wheel over the timeline: zoom, keeping the moment under the pointer where it is.
    private void OnZoomRequested(object? sender, (double X, int Delta) request)
    {
        double time = View.TimeAt(request.X);
        double viewportX = request.X - Scroller.HorizontalOffset;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value * Math.Pow(1.25, request.Delta / 120.0), ZoomSlider.Minimum, ZoomSlider.Maximum);
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(TimelineLayout.XOf(time, View.PixelsPerSecond) - viewportX);
    }

    // The plain wheel scrolls the timeline sideways.
    private void OnTimelineWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void EnsurePlayheadVisible()
    {
        double x = View.PlayheadX;
        double offset = Scroller.HorizontalOffset;
        double width = Scroller.ViewportWidth;
        if (x < offset + 24 || x > offset + width - 24)
        {
            Scroller.ScrollToHorizontalOffset(Math.Max(0, x - width * 0.25));
        }
    }

    // ---------- the playhead and playback

    private void SetPlayhead(double time, bool show = true)
    {
        _playhead = Math.Clamp(time, 0, _timeline?.Duration ?? 0);
        View.Playhead = _playhead;
        UpdatePositionText();
        if (show)
        {
            ShowFrame();
        }
    }

    private void UpdatePositionText() =>
        PositionText.Text = _timeline == null ? "" : $"{TimeText.Format(_playhead)} / {TimeText.Format(_timeline.Duration)}";

    // The player shows the picture of the original that plays at the playhead.
    private void ShowFrame()
    {
        if (_timeline == null || _playing)
        {
            return;
        }

        (int index, double at) = _timeline.Locate(_playhead);
        VideoSegment segment = _timeline.Segments[index];
        Player.Position = TimeSpan.FromSeconds(Math.Clamp(at, segment.Start, Math.Max(segment.Start, segment.End - 0.04)));
    }

    private void OnPlay(object sender, RoutedEventArgs e) => TogglePlayback();

    private void TogglePlayback()
    {
        if (_playing)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (!CanEdit)
        {
            return;
        }

        if (_playhead >= _timeline!.Duration - 0.05)
        {
            SetPlayhead(0, show: false); // at the end: play again from the beginning
        }

        (int index, double at) = _timeline.Locate(_playhead);
        _playIndex = index;
        Player.IsMuted = _timeline.Segments[index].Muted;
        Player.Position = TimeSpan.FromSeconds(at);
        _tailSince = null;
        _settleUntil = Environment.TickCount64 + 80;
        Player.Play();
        _playing = true;
        PlayButton.Content = "⏸ Pausa";
        _timer.Start();
    }

    private void StopPlayback()
    {
        if (_playing)
        {
            Player.Pause();
            _playing = false;
        }
        _timer.Stop();
        Player.IsMuted = false;
        PlayButton.Content = "▶ Reproducir";
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            FollowPlayback(pieceEnded: true); // the file ran out before the piece's end was reached
        }
    }

    // Where the player is in the original. Its clock stops at the whole seconds of the video's length (see OnOpened) while the
    // picture goes on to the true end, so over that last fraction of a second the position is counted from the clock instead.
    private double PlayerPosition()
    {
        double position = Player.Position.TotalSeconds;
        double whole = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan.TotalSeconds : double.MaxValue;
        if (position < whole - 0.001)
        {
            _tailSince = null;
            return position;
        }

        _tailSince ??= Stopwatch.GetTimestamp();
        return Math.Min(_timeline!.SourceDuration, whole + Stopwatch.GetElapsedTime(_tailSince.Value).TotalSeconds);
    }

    // Plays the pieces one after the other: when one ends the player jumps to where the next begins in the original.
    private void FollowPlayback(bool pieceEnded = false)
    {
        if (_timeline == null || !_playing || Environment.TickCount64 < _settleUntil && !pieceEnded)
        {
            return;
        }

        IReadOnlyList<VideoSegment> pieces = _timeline.Segments;
        double position = PlayerPosition();
        VideoSegment piece = pieces[_playIndex];
        if (pieceEnded || position >= piece.End - 0.02)
        {
            if (_playIndex + 1 >= pieces.Count)
            {
                StopPlayback();
                SetPlayhead(_timeline.Duration, show: false);
                return;
            }

            _playIndex++;
            VideoSegment next = pieces[_playIndex];
            Player.IsMuted = next.Muted;
            if (pieceEnded || Math.Abs(next.Start - piece.End) > 0.04)
            {
                Player.Position = TimeSpan.FromSeconds(next.Start);
                position = next.Start;
                _tailSince = null;
                _settleUntil = Environment.TickCount64 + 80;
                if (pieceEnded)
                {
                    Player.Play(); // the end of the file leaves the player stopped
                }
            }
            View.SelectedIndex = _playIndex;
            RefreshState();
        }

        _playhead = _timeline.ToTimeline(_playIndex, position);
        View.Playhead = _playhead;
        UpdatePositionText();
        EnsurePlayheadVisible();
    }

    private void OnStepBack(object sender, RoutedEventArgs e) => Step(-FrameSeconds);

    private void OnStepForward(object sender, RoutedEventArgs e) => Step(FrameSeconds);

    private void Step(double seconds)
    {
        if (!CanEdit)
        {
            return;
        }

        StopPlayback();
        SetPlayhead(_playhead + seconds);
        Select(_timeline!.Locate(_playhead).Index);
        EnsurePlayheadVisible();
    }

    // ---------- editing

    private void Select(int index)
    {
        View.SelectedIndex = index;
        RefreshState();
    }

    private void OnSplit(object sender, RoutedEventArgs e) => Split();

    private void Split()
    {
        if (!CanEdit)
        {
            return;
        }

        if (!_timeline!.Split(_playhead))
        {
            KeptText.Text = $"Ahí no se puede dividir: cada parte debe durar al menos {EditTimeline.MinSeconds:0.0} s.";
            return;
        }
        Select(_timeline.Locate(_playhead).Index); // the half that starts at the playhead
    }

    private void OnDelete(object sender, RoutedEventArgs e) => Delete();

    private void Delete()
    {
        if (!CanEdit)
        {
            return;
        }

        int index = View.SelectedIndex;
        if (!_timeline!.Delete(index))
        {
            return;
        }

        int next = Math.Min(index, _timeline.Segments.Count - 1);
        Select(next);
        SetPlayhead(_timeline.StartOf(next));
    }

    private void OnDuplicate(object sender, RoutedEventArgs e) => Duplicate();

    private void Duplicate()
    {
        if (!CanEdit)
        {
            return;
        }

        int index = View.SelectedIndex;
        if (_timeline!.Duplicate(index))
        {
            Select(index + 1);
            SetPlayhead(_timeline.StartOf(index + 1));
        }
    }

    private void OnMute(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        int index = View.SelectedIndex;
        if (CanEdit && index >= 0 && index < _timeline!.Segments.Count)
        {
            _timeline.SetMuted(index, !_timeline.Segments[index].Muted);
        }
    }

    private void OnMoveLeft(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveRight(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        if (!CanEdit)
        {
            return;
        }

        int index = View.SelectedIndex;
        int target = index + direction;
        if (_timeline!.Move(index, target))
        {
            Select(target);
            SetPlayhead(_timeline.StartOf(target));
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void Undo()
    {
        if (CanEdit && _timeline!.CanUndo)
        {
            _timeline.Undo();
            Select(_timeline.Locate(_playhead).Index);
        }
    }

    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void Redo()
    {
        if (CanEdit && _timeline!.CanRedo)
        {
            _timeline.Redo();
            Select(_timeline.Locate(_playhead).Index);
        }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (CanEdit && _timeline!.Reset())
        {
            Select(0);
            SetPlayhead(0);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!CanEdit)
        {
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        Key key = e.Key == Key.System ? e.SystemKey : e.Key; // with Alt held the key arrives as "System"

        switch (key)
        {
            case Key.Space:
                TogglePlayback();
                break;
            case Key.S when ctrl:
                OnSave(this, e);
                break;
            case Key.S:
                Split();
                break;
            case Key.Delete or Key.Back:
                Delete();
                break;
            case Key.D when ctrl:
                Duplicate();
                break;
            case Key.M when !ctrl:
                ToggleMute();
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.Left when alt:
                MoveSelected(-1);
                break;
            case Key.Right when alt:
                MoveSelected(1);
                break;
            case Key.Left:
                Step(shift ? -1 : -FrameSeconds);
                break;
            case Key.Right:
                Step(shift ? 1 : FrameSeconds);
                break;
            case Key.Home:
                StopPlayback();
                SetPlayhead(0);
                Select(0);
                EnsurePlayheadVisible();
                break;
            case Key.End:
                StopPlayback();
                SetPlayhead(_timeline!.Duration);
                Select(_timeline.Segments.Count - 1);
                EnsurePlayheadVisible();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---------- buttons, text, saving

    // What can be done now, from where the playhead and the selection are.
    private void RefreshState()
    {
        if (_timeline == null)
        {
            return;
        }

        int count = _timeline.Segments.Count;
        int selected = View.SelectedIndex;
        bool free = !_busy;
        bool hasSelection = free && selected >= 0 && selected < count;

        PlayButton.IsEnabled = BackButton.IsEnabled = ForwardButton.IsEnabled = ZoomSlider.IsEnabled = free;
        SplitButton.IsEnabled = free && _timeline.CanSplit(_playhead);
        DeleteButton.IsEnabled = hasSelection && count > 1;
        DuplicateButton.IsEnabled = hasSelection;
        MuteButton.IsEnabled = hasSelection;
        MoveLeftButton.IsEnabled = hasSelection && selected > 0;
        MoveRightButton.IsEnabled = hasSelection && selected < count - 1;
        UndoButton.IsEnabled = free && _timeline.CanUndo;
        RedoButton.IsEnabled = free && _timeline.CanRedo;
        ResetButton.IsEnabled = free && _timeline.IsEdited;
        SaveButton.IsEnabled = free && _timeline.IsEdited;
        CancelButton.IsEnabled = free;

        string muteText = hasSelection && _timeline.Segments[selected].Muted ? "Activar sonido" : "Silenciar";
        MuteButton.Content = muteText;
        MuteMenuItem.Header = muteText;
        UpdatePositionText();

        if (_busy)
        {
            KeptText.Text = "Guardando el video…";
        }
        else
        {
            KeptText.Text = _timeline.IsEdited
                ? $"Quedará de {TimeText.Format(_timeline.Duration)} (el original dura {TimeText.Format(_timeline.SourceDuration)}) · {count} {(count == 1 ? "parte" : "partes")}"
                : "Sin cambios. Divide con S, elimina con Supr y arrastra el título de una parte para cambiar su orden.";
        }

        SelectionText.Text = hasSelection
            ? $"Parte {selected + 1} de {count} · del original {TimeText.Format(_timeline.Segments[selected].Start)} a {TimeText.Format(_timeline.Segments[selected].End)}"
            : "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        WorkBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WorkBar.Value = 0;
        View.IsEnabled = !busy;
        RefreshState();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_timeline == null || _busy || !_timeline.IsEdited)
        {
            return;
        }

        StopPlayback();
        SetBusy(true);
        string target = NewFileName();
        try
        {
            var progress = new Progress<double>(value => WorkBar.Value = value);
            await VideoExporter.ExportAsync(_source, target, _timeline.Segments, progress);
            EditedPath = target;
            _saved = true;
            _busy = false; // or Closing would refuse
            DialogResult = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An async void handler: anything uncaught here would end the whole app.
            TryDelete(target);
            SetBusy(false);
            MessageBox.Show(this, $"No se pudo guardar el video: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Next to the recording, never over another file: name_editado.mp4, then name_editado_2.mp4...
    private string NewFileName()
    {
        string folder = Path.GetDirectoryName(_source)!;
        string name = Path.GetFileNameWithoutExtension(_source);
        string path = Path.Combine(folder, name + "_editado.mp4");
        for (int n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{name}_editado_{n}.mp4");
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true; // the file is being written
            return;
        }

        if (!_saved && _timeline?.IsEdited == true)
        {
            MessageBoxResult answer = MessageBox.Show(this, "Hay cambios sin guardar. ¿Quieres descartarlos?", "Zarpazo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            e.Cancel = answer != MessageBoxResult.Yes;
        }
    }
}
