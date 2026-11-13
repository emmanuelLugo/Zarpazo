using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;
using Shape = System.Windows.Shapes.Shape;
using System.Windows.Threading;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Zarpazo.Recording;

/// <summary>
/// A small video editor for a recording: cut it in pieces, delete the ones that are not wanted, change their order, repeat
/// or mute them, adjust where each one starts and ends, zoom in on what was clicked, and save the result as a new video.
/// </summary>
public partial class VideoEditWindow : Window
{
    private const double FrameSeconds = 1.0 / 30;
    private const int MaxOpenAttempts = 3;

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
    private int _openAttempts;
    private bool _busy;
    private readonly VideoDelivery _delivery;
    private string? _lastSaved; // where the video was last saved, for "Mostrar en la carpeta"
    private bool _closed;
    private CursorTrack? _track;
    private ZoomView _shownView = ZoomView.Full; // what the player shows now, to turn a click on it into a point of the picture
    private bool _aiming; // dragging on the picture to move where a zoom looks
    private bool _showingDepth;
    private long _playheadStamp; // when the playhead was last read from the player, to move the zoom smoothly in between
    private readonly Settings.SettingsStore? _settings;
    private VideoFrameStyle _frame;
    private CursorStyle _cursor;
    private CursorMotion? _motion; // the drawn pointer's path; null when this video has none to draw
    private bool _showingCursor;
    private ZoomStyle _zoomStyle;
    private bool _showingZoomStyle;
    private bool _lookFromControls; // the look is being changed by a panel, which shows the change itself
    private IReadOnlyList<VideoSegment>? _seenSegments; // what the window last saw of the edit, to tell a change of look alone
    private IReadOnlyList<ZoomRegion>? _seenZooms;
    private IReadOnlyList<VideoPreset> _presets;
    private long _presetsClosedAt; // see OnPresets
    private readonly List<Ellipse> _clickMarks = [];
    private FrameLayout? _layout; // where the recording sits in the scene; null without a frame
    private ZoomCamera? _previewCamera; // the zooms in the scene, for the preview
    private bool _showingFrame;
    private bool _scenePending; // the scene will be laid out again for the newest frame and crop
    private int _sourceWidth; // the recorded picture, in even pixels as the saved video reads it; 0 until it opens
    private int _sourceHeight;
    private CropArea? _area; // the crop the preview shows; null while it shows the whole recording
    private bool _showingCrop;
    private VideoCrop? _shownCrop; // the crop the scene was last laid out for
    private CropEdges _cropGrab; // the sides of the crop's rectangle being dragged on the picture
    private VideoCrop _cropGrabStart;
    private Point _cropGrabFrom;
    private bool _pointerPending; // the pointer will be drawn again for the newest style
    private readonly DispatcherTimer _frameSave = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <param name="settings">Where the frame chosen is remembered; none in tests.</param>
    /// <param name="delivery">Where the finished video goes when it is copied or saved; by default "name_editado.mp4" next to the source.</param>
    /// <param name="notice">A first line for the status, such as why the recording stopped.</param>
    internal VideoEditWindow(string source, Settings.SettingsStore? settings = null, VideoDelivery? delivery = null, string? notice = null)
    {
        InitializeComponent();
        Theme.TitleBarTheme.Follow(this);
        _settings = settings;
        _frame = settings?.Current.VideoFrame ?? VideoFrameStyle.Default;
        _cursor = settings?.Current.VideoCursor ?? CursorStyle.Default;
        _zoomStyle = settings?.Current.VideoZoom ?? ZoomStyle.Default;
        _presets = settings?.Current.Presets ?? [];
        _frameSave.Tick += (_, _) =>
        {
            _frameSave.Stop();
            _settings?.Save();
        };
        BuildFrameChoices();
        BuildCursorChoices();
        ShowZoomStyle();
        BuildPresetRows();
        _ready = true;
        _source = source;
        _delivery = delivery ?? new VideoDelivery(source, NewFileName(), settings);
        DeliveryText.Text = notice ?? "";
        TitleText.Text = Path.GetFileName(_delivery.ReadyPath);
        FitToScreen();
        _timer.Tick += (_, _) => FollowPlayback();
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) =>
        {
            _closed = true;
            CompositionTarget.Rendering -= OnRendering;
            _delivery.Finish();
            if (_frameSave.IsEnabled)
            {
                SaveCursorChoice(); // a slider let go just before closing
            }
            _timer.Stop();
            Player.Close(); // lets go of the file
        };
        Player.Source = new Uri(source);
        Player.Play(); // a first picture only shows once it has started; it is paused as soon as it opens
    }

    // As big as it was designed, but never bigger than the screen it opens on (a laptop at 125 % has 1536 x 824 to give).
    private void FitToScreen()
    {
        Rect work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height * 0.94));
        if (work.Width < MinWidth + 40 || work.Height < MinHeight + 40)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Where the finished video is made, for copying and saving.</summary>
    internal VideoDelivery Delivery => _delivery;

    /// <summary>Asks where to save; the tests answer it themselves. Null means the person changed their mind.</summary>
    internal Func<string?>? AskSavePath { get; set; }

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

            // The picture at the video's own size: the zoom, the frame and the crop are worked out in its pixels (the stage
            // takes the crop's size in ApplyFrame).
            int width = Player.NaturalVideoWidth > 0 ? Player.NaturalVideoWidth : (int)clip.GetVideoEncodingProperties().Width;
            int height = Player.NaturalVideoHeight > 0 ? Player.NaturalVideoHeight : (int)clip.GetVideoEncodingProperties().Height;
            Player.Width = width;
            Player.Height = height;
            _sourceWidth = width & ~1;
            _sourceHeight = height & ~1;

            _timeline = new EditTimeline(duration);
            _timeline.StartWithLook(Look); // the style remembered from the last video: undo goes back to it, not further
            View.Timeline = _timeline;
            Lane.Timeline = _timeline; // before the window listens, so the zooms are placed again before the window looks at them
            _timeline.Changed += OnTimelineChanged;
            _track = CursorTrack.Load(_source);
            if (_track != null)
            {
                _timeline.StartWithZooms(ZoomSuggester.Suggest(_track, duration, _zoomStyle.Depth)); // like Recordly: the clicks come zoomed already
            }
            RebuildPointer();
            ShowCursorChoices();
            ShowFrameValues(); // the corners in pixels of this video
            ShowCropChoices();
            ApplyFrame();
            View.SelectedIndex = 0;
            SetPlayhead(0);
            UpdateScale();
            RefreshState();
            await LoadThumbnailsAsync(clip);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            KeptText.Text = $"No se pudo abrir el video para editarlo: {ex.Message}";
        }
    }

    // Windows' player now and then gives up opening a video with sound (E_UNEXPECTED, right after reading its size); a
    // second try opens it. Only when that fails too is the person told.
    private async void OnFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_timeline == null && _openAttempts < MaxOpenAttempts && !_closed)
        {
            _openAttempts++;
            Player.Close();
            await Task.Delay(250);
            if (!_closed)
            {
                Player.Source = new Uri(_source);
                Player.Play();
            }
            return;
        }
        KeptText.Text = $"No se pudo abrir el video para editarlo: {e.ErrorException?.Message}";
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
        // A slider or a drag on the picture sends a change for every pixel it moves, so each kind of change does only its own
        // work. A new look (from a panel, a preset, undo or redo) and a new crop are laid out once per frame drawn; when the
        // pieces and the zooms are the same lists as before, that is all, and the video playing is not stopped.
        bool piecesChanged = !ReferenceEquals(_timeline!.Segments, _seenSegments);
        bool zoomsChanged = !ReferenceEquals(_timeline.Zooms, _seenZooms);
        _seenSegments = _timeline.Segments;
        _seenZooms = _timeline.Zooms;
        if (_timeline.Look != Look)
        {
            TakeLook(_timeline.Look, _lookFromControls);
        }
        if (_timeline.Crop != _shownCrop)
        {
            ShowCropChoices(); // an undone crop puts its rows back
            ScheduleScene();
        }
        if (!piecesChanged && !zoomsChanged)
        {
            RefreshState();
            return;
        }

        RebuildPreviewCamera();
        StopPlayback();
        View.SelectedIndex = Math.Clamp(View.SelectedIndex, 0, _timeline!.Segments.Count - 1);
        _playhead = Math.Clamp(_playhead, 0, _timeline.Duration);
        View.Playhead = Lane.Playhead = _playhead;
        if (!View.IsDragging && !Lane.IsDragging)
        {
            UpdateScale();
            if (piecesChanged)
            {
                ShowFrame(); // the picture at the playhead is another only when the pieces changed: a seek costs a decode
            }
        }
        ApplyZoomPreview();
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
        ApplyZoomPreview();
        RefreshState();
    }

    // The whole edit fits the width at zoom 1; more than that and the timeline scrolls.
    private void UpdateScale()
    {
        if (_timeline == null || View.IsDragging || Lane.IsDragging)
        {
            return;
        }

        double viewport = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth;
        if (viewport <= 0)
        {
            return;
        }

        View.MinWidth = Lane.MinWidth = viewport;
        View.PixelsPerSecond = (viewport - TimelineLayout.Padding * 2 - 1) / Math.Max(0.1, _timeline.Duration) * _zoom;
        Lane.PixelsPerSecond = View.PixelsPerSecond;
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
        View.Playhead = Lane.Playhead = _playhead;
        UpdatePositionText();
        if (show)
        {
            ShowFrame();
        }
        ApplyZoomPreview();
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
        ApplyPieceSound(_timeline.Segments[index]);
        Player.Position = TimeSpan.FromSeconds(at);
        _tailSince = null;
        _settleUntil = Environment.TickCount64 + 80;
        Player.Play();
        _playheadStamp = Stopwatch.GetTimestamp();
        _playing = true;
        PlayIcon.Data = (Geometry)FindResource("PauseGlyph");
        PlayButton.ToolTip = "Pausar (Espacio)";
        _timer.Start();
        if (CropTab.IsChecked == true)
        {
            ApplyFrame(); // the crop being chosen plays as it will look
        }
        ApplyZoomPreview(); // a chosen zoom stops showing the whole picture: it plays as it will look
    }

    private void StopPlayback()
    {
        if (_playing)
        {
            Player.Pause();
            _playing = false;
            if (CropTab.IsChecked == true)
            {
                ApplyFrame(); // back to the whole recording, to go on choosing the crop
                ApplyZoomPreview();
            }
        }
        _timer.Stop();
        Player.IsMuted = false;
        Player.SpeedRatio = 1;
        PlayIcon.Data = (Geometry)FindResource("PlayGlyph");
        PlayButton.ToolTip = "Reproducir lo que quedará (Espacio)";
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
        double rate = _playing && _playIndex < _timeline!.Segments.Count ? _timeline.Segments[_playIndex].Rate : 1;
        return Math.Min(_timeline!.SourceDuration, whole + Stopwatch.GetElapsedTime(_tailSince.Value).TotalSeconds * rate);
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
            ApplyPieceSound(next);
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
        _playheadStamp = Stopwatch.GetTimestamp();
        View.Playhead = Lane.Playhead = _playhead;
        UpdatePositionText();
        EnsurePlayheadVisible();
    }

    // A piece plays at its speed, and without sound when muted or played faster or slower (as in the saved video).
    private void ApplyPieceSound(VideoSegment piece)
    {
        Player.IsMuted = piece.IsSilent;
        Player.SpeedRatio = piece.Rate;
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

    // Declared on the button in the window, so it takes the window's dark menu style; a click opens it like a right-click.
    private void OnSpeedButton(object sender, RoutedEventArgs e)
    {
        FillSpeedMenu(SpeedMenu.Items); // before it opens, so it opens at its size
        SpeedMenu.PlacementTarget = SpeedButton;
        SpeedMenu.IsOpen = true;
    }

    private void OnSpeedMenuOpened(object sender, RoutedEventArgs e) => FillSpeedMenu(SpeedMenu.Items);

    // The speeds offered, the selected piece's checked. Faster or slower pieces play without sound, as in the saved video.
    private void FillSpeedMenu(ItemCollection items)
    {
        items.Clear();
        int index = View.SelectedIndex;
        double current = _timeline != null && index >= 0 && index < _timeline.Segments.Count ? _timeline.Segments[index].Rate : 1;
        foreach (double speed in VideoSegment.Speeds)
        {
            var item = new MenuItem
            {
                Header = speed == 1 ? "1× (normal)" : TimeText.Speed(speed),
                IsCheckable = true,
                IsChecked = speed == current,
            };
            item.Click += (_, _) => SetSelectedSpeed(speed);
            items.Add(item);
        }
        items.Add(new Separator());
        items.Add(new MenuItem { Header = "A otra velocidad, la parte va sin sonido", IsEnabled = false });
    }

    private void SetSelectedSpeed(double speed)
    {
        int index = View.SelectedIndex;
        if (CanEdit && index >= 0 && index < _timeline!.Segments.Count && _timeline.SetSpeed(index, speed) && _playing && index == _playIndex)
        {
            ApplyPieceSound(_timeline.Segments[index]); // the piece playing now changes pace at once
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
            case Key.Delete or Key.Back when Lane.SelectedZoom >= 0:
                DeleteZoom();
                break;
            case Key.Delete or Key.Back:
                Delete();
                break;
            case Key.Z when !ctrl:
                AddZoomAt(_playhead);
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
        SpeedButton.IsEnabled = SpeedMenuItem.IsEnabled = hasSelection;
        MoveLeftButton.IsEnabled = hasSelection && selected > 0;
        MoveRightButton.IsEnabled = hasSelection && selected < count - 1;
        UndoButton.IsEnabled = free && _timeline.CanUndo;
        RedoButton.IsEnabled = free && _timeline.CanRedo;
        ResetButton.IsEnabled = free && _timeline.IsEdited;
        CopyButton.IsEnabled = SaveButton.IsEnabled = free;

        int zooms = _timeline.Zooms.Count;
        int chosenZoom = Lane.SelectedZoom;
        AutoZoomButton.IsEnabled = free && _track != null;
        AutoZoomButton.ToolTip = _track != null
            ? "Pone un zoom en cada grupo de clics de la grabación. Reemplaza los zooms que haya (se puede deshacer)"
            : "Esta grabación no guardó dónde se hizo clic (se grabó con una versión anterior de Zarpazo). Puedes añadir zooms a mano";
        AddZoomButton.IsEnabled = free;
        ZoomDetails.Visibility = free && chosenZoom >= 0 && chosenZoom < zooms ? Visibility.Visible : Visibility.Collapsed;
        if (ZoomDetails.Visibility == Visibility.Visible)
        {
            ShowDepth(_timeline.Zooms[chosenZoom].Scale);
        }
        string zoomText = (zooms switch { 0 => "", 1 => " · 1 zoom", _ => $" · {zooms} zooms" }) + (_frame.Enabled ? " · con marco" : "")
            + (_timeline.Crop.IsNone ? "" : " · recortado");

        bool muted = hasSelection && _timeline.Segments[selected].Muted;
        string muteText = muted ? "Activar sonido" : "Silenciar";
        MuteIcon.Data = (Geometry)FindResource(muted ? "MutedGlyph" : "SoundGlyph");
        MuteButton.ToolTip = $"{muteText} la parte seleccionada (M)";
        MuteMenuItem.Header = muteText;
        double speed = hasSelection ? _timeline.Segments[selected].Rate : 1;
        SpeedText.Text = TimeText.Speed(speed);
        SpeedText.ClearValue(TextBlock.ForegroundProperty);
        if (speed != 1)
        {
            SpeedText.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        }
        FillSpeedMenu(SpeedMenuItem.Items);
        UpdatePositionText();

        if (_busy)
        {
            KeptText.Text = "Preparando el video… se vuelve a crear cada cuadro con el cursor, el zoom y el marco: tarda más o menos lo que dura.";
        }
        else
        {
            KeptText.Text = _timeline.IsEdited
                ? $"Quedará de {TimeText.Format(_timeline.Duration)} (el original dura {TimeText.Format(_timeline.SourceDuration)}) · {count} {(count == 1 ? "parte" : "partes")}{zoomText}"
                : zooms > 0 || _frame.Enabled || !_timeline.Crop.IsNone
                    ? $"Sin cortes{zoomText}. Divide con S, elimina con Supr; arrastra un zoom para moverlo o sus bordes para alargarlo."
                    : "Sin cambios. Divide con S, elimina con Supr y arrastra el título de una parte para cambiar su orden.";
        }

        SelectionText.Text = hasSelection
            ? $"Parte {selected + 1} de {count} · del original {TimeText.Format(_timeline.Segments[selected].Start)} a {TimeText.Format(_timeline.Segments[selected].End)}"
            : "";
    }

    // The recording has to be made again: pieces cut or moved, zooms (the automatic ones count), a frame, a pointer to draw,
    // or a crop. Otherwise the finished video is the recording itself.
    internal bool NeedsRendering => _timeline != null
        && (_timeline.IsEdited || _timeline.Zooms.Count > 0 || _frame.Enabled || !_timeline.Crop.IsNone
            || CursorLayer.For(_track, _cursor, _timeline.Segments) != null);

    // Every choice that changes the finished video: when it is the same as last time, the video already made is used again.
    private string Recipe => _timeline == null ? "" : string.Join("|",
        string.Join(";", _timeline.Segments), string.Join(";", _timeline.Zooms), _frame, CursorLayer.For(_track, _cursor, _timeline.Segments) != null ? _cursor : "",
        _timeline.Crop, _timeline.Zooms.Count > 0 ? _zoomStyle.Motion : "");

    private void SetBusy(bool busy)
    {
        _busy = busy;
        WorkBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WorkBar.Value = 0;
        View.IsEnabled = Lane.IsEnabled = Stage.IsEnabled = !busy;
        RefreshState();
    }

    // Makes the finished video, or takes the one already made with the same choices; null if it failed (the person is told).
    private async Task<string?> PrepareAsync()
    {
        if (_timeline == null || _busy)
        {
            return null;
        }

        StopPlayback();
        bool reused = _delivery.IsCurrent(Recipe);
        if (!reused)
        {
            SetBusy(true);
            DeliveryText.Text = NeedsRendering ? "Preparando el video…" : "";
            ShowInFolderButton.Visibility = Visibility.Collapsed;
        }
        try
        {
            var progress = new Progress<double>(value => WorkBar.Value = value);
            return await _delivery.PrepareAsync(Recipe, async target =>
            {
                if (NeedsRendering)
                {
                    await VideoExporter.ExportAsync(_source, target, _timeline.Segments, _timeline.Zooms, _timeline.SourceDuration, _frame, progress,
                        _track, _cursor, _timeline.Crop, _zoomStyle.Motion);
                }
                else
                {
                    File.Copy(_source, target, overwrite: true); // nothing to change: the recording as it is
                }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Called from async void handlers: anything uncaught here would end the whole app.
            DeliveryText.Text = $"No se pudo preparar el video: {ex.Message}";
            return null;
        }
        finally
        {
            if (!reused)
            {
                SetBusy(false);
            }
        }
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (await PrepareAsync() is not { } path)
        {
            return;
        }

        DeliveryText.Text = VideoDelivery.CopyToClipboard(path)
            ? $"Copiado · {SizeText(path)}. Pégalo en un chat o un documento."
            : "No se pudo copiar: otro programa usa el portapapeles. Prueba otra vez.";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_timeline == null || _busy || AskWhereToSave() is not { } chosen)
        {
            return;
        }
        if (await PrepareAsync() is not { } path)
        {
            return;
        }

        try
        {
            if (!string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(path, chosen, overwrite: true);
            }
            _lastSaved = chosen;
            DeliveryText.Text = $"Guardado · {SizeText(chosen)} · {Path.GetFileName(chosen)}";
            ShowInFolderButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeliveryText.Text = $"No se pudo guardar: {ex.Message}";
        }
    }

    private string? AskWhereToSave()
    {
        if (AskSavePath != null)
        {
            return AskSavePath();
        }

        Settings.AppSettings? current = _settings?.Current;
        string folder = current != null && Directory.Exists(current.SaveFolder) ? current.SaveFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar video",
            FileName = current != null
                ? Output.FileNameTemplate.Expand(current.FileNameTemplate, DateTime.Now, current.AutoSaveCounter + 1)
                : Path.GetFileNameWithoutExtension(_delivery.ReadyPath),
            Filter = "Video MP4 (*.mp4)|*.mp4",
            AddExtension = true,
            DefaultExt = ".mp4",
            InitialDirectory = folder,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (_lastSaved != null && File.Exists(_lastSaved))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastSaved}\"") { UseShellExecute = true });
        }
    }

    private static string SizeText(string path)
    {
        long bytes = new FileInfo(path).Length;
        return bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }

    // Next to the recording, never over another file: name_editado.mp4, then name_editado_2.mp4...
    private string NewFileName()
    {
        string folder = Path.GetDirectoryName(_source)!;
        string name = RecordingFiles.WithoutOriginalSuffix(Path.GetFileNameWithoutExtension(_source));
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


    // ---------- zoom

    /// <summary>The pointer recorded with the video, for the automatic zooms; null for an older recording.</summary>
    internal CursorTrack? Track => _track;

    // The player shows what the edited video will show at the playhead: zoomed in, or the whole picture. While a zoom is
    // chosen (and not playing) it shows the whole picture with the zoomed part marked, so where it looks can be changed.
    // The player's position is read every 20 ms or so, which moved the zoom in small jumps; at every picture the screen
    // draws, the playhead is carried on from the last reading by the time that has passed.
    private void OnRendering(object? sender, EventArgs e)
    {
        if (_playing && (_previewCamera is { IsEmpty: false } || _motion != null))
        {
            double since = Math.Min(0.1, Stopwatch.GetElapsedTime(_playheadStamp).TotalSeconds);
            ApplyZoomPreview(_playhead + since);
        }
    }

    private void ApplyZoomPreview() => ApplyZoomPreview(_playhead);

    private void ApplyZoomPreview(double time)
    {
        ZoomCamera? camera = _previewCamera;
        double width = Stage.Width, height = Stage.Height;
        if (camera == null || double.IsNaN(width) || double.IsNaN(height))
        {
            return;
        }

        int chosen = Lane.SelectedZoom;
        bool cropping = CropEditing;
        bool picking = chosen >= 0 && !_playing && !cropping && _timeline != null && chosen < _timeline.Zooms.Count;
        ZoomView view = picking || cropping ? ZoomView.Full : camera.ViewAt(time);
        _shownView = view;
        UpdatePointer(time, view);
        Scene.RenderTransform = view.IsFull
            ? Transform.Identity
            : new MatrixTransform(1 / view.Width, 0, 0, 1 / view.Height, -view.X * width / view.Width, -view.Y * height / view.Height);

        FocusLayer.Visibility = picking ? Visibility.Visible : Visibility.Collapsed;
        if (!picking)
        {
            return;
        }

        // The zoom's point is on the original recording: into the crop, then into the frame (see CropArea).
        ZoomRegion zoom = _timeline!.Zooms[chosen];
        if (_area is { } area)
        {
            zoom = area.ToCrop([zoom])[0];
        }
        if (_layout is { } layout)
        {
            (double sceneX, double sceneY) = layout.ToScene(zoom.FocusX, zoom.FocusY);
            zoom = zoom with { FocusX = sceneX, FocusY = sceneY };
        }
        double half = 0.5 / zoom.Scale;
        ZoomView target = ZoomView.Around(zoom.Scale, Math.Clamp(zoom.FocusX, half, 1 - half), Math.Clamp(zoom.FocusY, half, 1 - half));
        var frame = new Rect(target.X * width, target.Y * height, target.Width * width, target.Height * height);
        double line = Math.Max(2, width / 320); // the picture is shrunk to fit, so the lines are drawn thicker in its pixels
        FocusShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, width, height)), new RectangleGeometry(frame));
        FocusFrame.StrokeThickness = line;
        FocusFrame.Width = frame.Width;
        FocusFrame.Height = frame.Height;
        Canvas.SetLeft(FocusFrame, frame.X);
        Canvas.SetTop(FocusFrame, frame.Y);
        double dot = line * 5;
        FocusDot.Width = FocusDot.Height = dot;
        FocusDot.StrokeThickness = line / 2;
        Canvas.SetLeft(FocusDot, zoom.FocusX * width - dot / 2);
        Canvas.SetTop(FocusDot, zoom.FocusY * height - dot / 2);
    }

    private void OnZoomSelectionChanged(object? sender, EventArgs e)
    {
        if (Lane.SelectedZoom >= 0)
        {
            StopPlayback(); // choosing a zoom shows the whole picture to pick where it looks
            ZoomTab.IsChecked = true; // and its panel, to change how close it goes
        }
        ApplyZoomPreview();
        RefreshState();
    }

    private void OnTimelineScrubbed(object? sender, double time)
    {
        Lane.SelectedZoom = -1; // working on the pieces: Supr and the buttons act on them again
        OnScrubbed(sender, time);
    }

    private void OnZoomAddRequested(object? sender, double time) => AddZoomAt(time);

    private void OnAddZoom(object sender, RoutedEventArgs e) => AddZoomAt(_playhead);

    // A zoom of two seconds around a moment of the edited video, looking at where the pointer was then.
    private void AddZoomAt(double time)
    {
        if (!CanEdit)
        {
            return;
        }

        StopPlayback();
        (_, double at) = _timeline!.Locate(time);
        (double X, double Y) focus = _track?.PositionAt((long)(at * 1000)) is { } pointer
            ? (Math.Clamp(pointer.X, 0, 1), Math.Clamp(pointer.Y, 0, 1))
            : (0.5, 0.5);
        int index = _timeline.AddZoom(new ZoomRegion(at - 1.25, at + 1.25, _zoomStyle.Depth, focus.X, focus.Y));
        if (index < 0)
        {
            KeptText.Text = "Ahí ya hay un zoom o no queda sitio para otro. Elige otro momento.";
            return;
        }
        Lane.SelectedZoom = index;
        SetPlayhead(time);
    }

    private void OnAutoZoom(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _track == null)
        {
            return;
        }

        StopPlayback();
        IReadOnlyList<ZoomRegion> zooms = ZoomSuggester.Suggest(_track, _timeline!.SourceDuration, _zoomStyle.Depth);
        if (zooms.Count == 0)
        {
            KeptText.Text = "No hay clics dentro de la zona grabada, así que no hay dónde hacer zoom.";
            return;
        }
        Lane.SelectedZoom = -1;
        _timeline.SetZooms(zooms);
    }

    private void OnDeleteZoom(object sender, RoutedEventArgs e) => DeleteZoom();

    private void DeleteZoom()
    {
        int index = Lane.SelectedZoom;
        if (CanEdit && index >= 0)
        {
            Lane.SelectedZoom = -1;
            _timeline!.DeleteZoom(index);
        }
    }

    private void OnZoomDepthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _showingDepth || !CanEdit || Lane.SelectedZoom < 0 || ZoomDepthBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }
        _timeline!.SetZoomScale(Lane.SelectedZoom, double.Parse(tag, CultureInfo.InvariantCulture));
    }

    // Clicking or dragging on the picture moves where the zoom looks: the chosen one, or the one playing at the playhead.
    private void OnStageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanEdit)
        {
            return;
        }
        if (CropEditing)
        {
            GrabCrop(e);
            return;
        }

        int index = Lane.SelectedZoom;
        if (index < 0 && Lane.Camera?.Spans.FirstOrDefault(span => _playhead >= span.Start && _playhead < span.End) is { End: > 0 } span)
        {
            index = span.Index;
        }
        if (index < 0)
        {
            KeptText.Text = "Elige un zoom en su fila (o añade uno con Z) para decidir adónde mira.";
            return;
        }

        StopPlayback();
        Lane.SelectedZoom = index;
        _aiming = true;
        _timeline!.BeginGesture(); // the whole drag is one step to undo
        Stage.CaptureMouse();
        AimAt(e.GetPosition(Stage));
        e.Handled = true;
    }

    private void OnStageMouseMove(object sender, MouseEventArgs e)
    {
        if (_aiming)
        {
            AimAt(e.GetPosition(Stage));
        }
        else if (_cropGrab != CropEdges.None)
        {
            DragCrop(e.GetPosition(Stage));
        }
        else
        {
            // Over the crop's rectangle the pointer shows what a press would grab.
            Stage.Cursor = CropEditing && CanEdit ? CropCursor(CropDrag.HitTest(_timeline!.Crop, e.GetPosition(Stage).X / Stage.Width,
                e.GetPosition(Stage).Y / Stage.Height, CropReach().X, CropReach().Y)) : null;
        }
    }

    private void OnStageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropGrab != CropEdges.None)
        {
            _cropGrab = CropEdges.None;
            _timeline?.EndGesture();
            Stage.ReleaseMouseCapture();
            return;
        }
        if (!_aiming)
        {
            return;
        }
        _aiming = false;
        _timeline?.EndGesture();
        Stage.ReleaseMouseCapture();
    }

    // A point of the player, in pixels of the video as it is shown, to shares of the whole picture.
    private void AimAt(Point point)
    {
        int index = Lane.SelectedZoom;
        if (_timeline == null || index < 0 || double.IsNaN(Stage.Width))
        {
            return;
        }

        double x = _shownView.X + point.X / Stage.Width * _shownView.Width;
        double y = _shownView.Y + point.Y / Stage.Height * _shownView.Height;
        if (_layout is { } layout)
        {
            // A point of the scene, as a point of the recording in it.
            x = (x * layout.Width - layout.Content.X) / layout.Content.Width;
            y = (y * layout.Height - layout.Content.Y) / layout.Content.Height;
        }
        if (_area is { } area)
        {
            (x, y) = area.FromCrop(x, y); // zooms keep points of the whole recording
        }
        _timeline.SetZoomFocus(index, x, y);
        ApplyZoomPreview();
    }

    // Shows the chosen zoom's depth without that counting as a change.
    private void ShowDepth(double scale)
    {
        _showingDepth = true;
        ZoomDepthBox.SelectedItem = ZoomDepthBox.Items.Cast<ComboBoxItem>()
            .MinBy(item => Math.Abs(double.Parse((string)item.Tag, CultureInfo.InvariantCulture) - scale));
        _showingDepth = false;
    }


    // ---------- frame

    // The swatches of the backgrounds on offer, the same as Embellecer's.
    private void BuildFrameChoices()
    {
        foreach (Output.BeautifyBackground background in Output.BeautifyBackground.All)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "frameBackground",
                Background = background.ToBrush(),
                Tag = background.Key,
                ToolTip = background.Name,
            };
            swatch.Checked += OnFrameChanged;
            FrameBackgrounds.Children.Add(swatch);
        }
        ShowFrameChoices();
    }

    private void ShowFrameChoices()
    {
        _showingFrame = true;
        FrameBox.IsChecked = _frame.Enabled;
        foreach (RadioButton swatch in FrameBackgrounds.Children.OfType<RadioButton>())
        {
            swatch.IsChecked = (string)swatch.Tag == _frame.Background;
        }
        FramePaddingSlider.Value = _frame.Padding;
        FrameRadiusSlider.Value = _frame.Radius;
        FrameShadowSlider.Value = _frame.Shadow;
        ShowFrameValues();
        FrameOptions.IsEnabled = _frame.Enabled;
        FrameOptions.Opacity = _frame.Enabled ? 1 : 0.5;
        FrameResetButton.IsEnabled = _frame with { Enabled = false } != VideoFrameStyle.Default with { Enabled = false };
        _showingFrame = false;
    }

    // The room as a share of the picture, the corners in pixels of the video, the shadow as how dark it is.
    private void ShowFrameValues()
    {
        double height = OutputArea() is { } area ? area.Height : 1080;
        FramePaddingText.Text = $"{Math.Round(_frame.Padding * 100)} %";
        FrameRadiusText.Text = $"{Math.Round(_frame.Radius * height)} px";
        FrameShadowText.Text = $"{Math.Round(_frame.Shadow * 100)} %";
    }

    private void OnFrameSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => OnFrameChanged(sender, e);

    private void OnFrameReset(object sender, RoutedEventArgs e)
    {
        SetFrame(VideoFrameStyle.Default with { Enabled = _frame.Enabled });
        ShowFrameChoices();
    }

    private void OnFrameChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _showingFrame)
        {
            return;
        }

        string background = FrameBackgrounds.Children.OfType<RadioButton>().FirstOrDefault(swatch => swatch.IsChecked == true)?.Tag as string
            ?? _frame.Background;
        SetFrame(new VideoFrameStyle(
            FrameBox.IsChecked == true,
            background,
            Math.Round(FramePaddingSlider.Value, 3),
            Math.Round(FrameRadiusSlider.Value, 4),
            Math.Round(FrameShadowSlider.Value * 20) / 20).Tidied());
        if (sender is Slider)
        {
            ShowFrameValues(); // a slider is left alone while it is dragged
            FrameResetButton.IsEnabled = true;
        }
        else
        {
            ShowFrameChoices();
        }
    }

    // Takes a new frame: remembered for the next video (written a moment after the last change), and shown. A slider sends
    // a change for every pixel it moves; the scene is laid out again once per frame drawn, with the latest values.
    private void SetFrame(VideoFrameStyle frame) => ChangeLook(Look with { Frame = frame });

    // A slider sends a change for every pixel it moves: the scene is laid out again once per frame drawn, with the latest values.
    private void ScheduleScene()
    {
        if (_scenePending)
        {
            return;
        }
        _scenePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _scenePending = false;
            ApplyFrame();
            ApplyZoomPreview();
            RefreshState();
        });
    }

    // The part of the recording the finished video shows, and so its size; null before the video opens.
    private CropArea? OutputArea() =>
        _sourceWidth > 0 ? CropArea.For(_timeline?.Crop ?? VideoCrop.None, _sourceWidth, _sourceHeight) : null;

    // Lays out the scene: the stage takes the finished video's size (the crop's), the recording is shifted so the part kept
    // fills it, and with a frame there is the backdrop behind and the recording in its place with its corners rounded.
    // Only sizes, transforms and clips change, which the graphics card applies: nothing is drawn again by the processor.
    // While the crop is being chosen the whole recording is shown instead, with the crop's rectangle over it.
    private void ApplyFrame()
    {
        if (_sourceWidth <= 0)
        {
            return;
        }

        bool cropping = CropEditing;
        _shownCrop = _timeline?.Crop;
        CropArea area = cropping ? CropArea.For(VideoCrop.None, _sourceWidth, _sourceHeight) : OutputArea()!.Value;
        _area = area.IsWhole ? null : area;
        double width = area.Width, height = area.Height;
        Stage.Width = Scene.Width = ContentHost.Width = width;
        Stage.Height = Scene.Height = ContentHost.Height = height;
        Canvas.SetLeft(Player, -area.Pixels.X);
        Canvas.SetTop(Player, -area.Pixels.Y);
        Geometry? cropClip = area.IsWhole ? null : new RectangleGeometry(new Rect(0, 0, width, height));

        CropLayer.Visibility = cropping ? Visibility.Visible : Visibility.Collapsed;
        if (cropping)
        {
            DrawCropRectangle();
        }
        UpdatePointerVisibility();

        if (_frame.Enabled && !cropping)
        {
            // The recording keeps its size and is moved and shrunk into its place: a transform, which the graphics card
            // applies, rather than a new size for the player.
            FrameLayout layout = FrameLayout.For(_frame, (int)width, (int)height);
            _layout = layout;
            Int32Rect content = layout.Content;
            double scaleX = content.Width / width, scaleY = content.Height / height;
            ContentHost.RenderTransform = new MatrixTransform(scaleX, 0, 0, scaleY, content.X, content.Y);
            double radius = Math.Min(layout.RadiusPixels, Math.Min(content.Width, content.Height) / 2);
            ContentHost.Clip = radius > 0 ? new RectangleGeometry(new Rect(0, 0, width, height), radius / scaleX, radius / scaleY) : cropClip;

            if (!ReferenceEquals(BackdropFill.Tag, _frame.Background))
            {
                BackdropFill.Background = Output.BeautifyBackground.Find(_frame.Background).ToBrush();
                BackdropFill.Tag = _frame.Background;
            }
            ShadowCard.Margin = new Thickness(content.X, content.Y, 0, 0);
            ShadowCard.Width = content.Width;
            ShadowCard.Height = content.Height;
            ShadowCard.CornerRadius = new CornerRadius(radius);
            ShadowCard.Visibility = _frame.Shadow > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShadowCard.Effect = _frame.Shadow > 0 ? FrameBackdrop.Shadow(_frame, layout, 1) : null;
            Backdrop.Visibility = Visibility.Visible;
        }
        else
        {
            _layout = null;
            Backdrop.Visibility = Visibility.Collapsed;
            ContentHost.RenderTransform = Transform.Identity;
            ContentHost.Clip = cropClip;
        }
        RebuildPreviewCamera();
    }

    // The camera of the preview: the timeline's zooms (on the original recording), with their points moved into the crop
    // and then into the scene when there is a frame, as VideoExporter does for the saved video.
    private void RebuildPreviewCamera()
    {
        if (_timeline == null)
        {
            return;
        }
        IReadOnlyList<ZoomRegion> zooms = _area is { } area ? area.ToCrop(_timeline.Zooms) : _timeline.Zooms;
        if (_layout is { } layout)
        {
            zooms = layout.ToScene(zooms);
        }
        _previewCamera = new ZoomCamera(_timeline.Segments, zooms, _zoomStyle.Motion);
    }

    // ---------- crop

    // The crop is chosen on the whole recording while its panel is open; playing shows the result.
    private bool CropEditing => CropTab.IsChecked == true && !_playing && _timeline != null;

    private void ShowCropChoices()
    {
        if (_timeline == null)
        {
            CropPanel.IsEnabled = false;
            return;
        }

        _showingCrop = true;
        VideoCrop crop = _timeline.Crop;
        CropPanel.IsEnabled = true;
        CropTopSlider.Value = crop.Top;
        CropBottomSlider.Value = crop.Bottom;
        CropLeftSlider.Value = crop.Left;
        CropRightSlider.Value = crop.Right;
        CropTopText.Text = Percent(crop.Top);
        CropBottomText.Text = Percent(crop.Bottom);
        CropLeftText.Text = Percent(crop.Left);
        CropRightText.Text = Percent(crop.Right);
        CropResetButton.IsEnabled = !crop.IsNone;
        CropSizeText.Text = OutputArea() is { } area
            ? crop.IsNone ? $"Imagen completa · {area.Width} × {area.Height} px" : $"El video quedará de {area.Width} × {area.Height} px"
            : "";
        _showingCrop = false;
    }

    private static string Percent(double share) => $"{(share * 100).ToString("0.#", CultureInfo.CurrentCulture)} %";

    private void OnCropSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _showingCrop || !CanEdit || sender is not Slider { Tag: CropEdges edge })
        {
            return;
        }
        _timeline!.SetCrop(_timeline.Crop.With(edge, Math.Round(e.NewValue, 4)));
    }

    // A drag along any row of the panels is one step to undo, like a drag on the picture.
    private void OnPanelRowsDown(object sender, MouseButtonEventArgs e)
    {
        if (CanEdit && e.OriginalSource is DependencyObject source && FindSlider(source) != null)
        {
            _timeline!.BeginGesture();
        }
    }

    private void OnPanelRowsUp(object sender, RoutedEventArgs e) => _timeline?.EndGesture();

    private static Slider? FindSlider(DependencyObject? element)
    {
        while (element != null && element is not Slider)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return element as Slider;
    }

    private void OnCropReset(object sender, RoutedEventArgs e)
    {
        if (CanEdit)
        {
            _timeline!.SetCrop(VideoCrop.None);
        }
    }

    // How close to a side of the rectangle a press grabs it: about ten pixels of the screen, in shares of the picture.
    private (double X, double Y) CropReach()
    {
        double shown = StageBox.ActualWidth > 0 ? StageBox.ActualWidth : Stage.Width;
        double share = 10 / Math.Max(1, shown);
        return (share, share * Stage.Width / Stage.Height);
    }

    private void GrabCrop(MouseButtonEventArgs e)
    {
        Point at = e.GetPosition(Stage);
        var point = new Point(at.X / Stage.Width, at.Y / Stage.Height);
        (double reachX, double reachY) = CropReach();
        CropEdges edges = CropDrag.HitTest(_timeline!.Crop, point.X, point.Y, reachX, reachY);
        if (edges == CropEdges.None)
        {
            return;
        }

        StopPlayback();
        _cropGrab = edges;
        _cropGrabStart = _timeline.Crop;
        _cropGrabFrom = point;
        _timeline.BeginGesture(); // the whole drag is one step to undo
        Stage.CaptureMouse();
        e.Handled = true;
    }

    private void DragCrop(Point at)
    {
        double dx = at.X / Stage.Width - _cropGrabFrom.X;
        double dy = at.Y / Stage.Height - _cropGrabFrom.Y;
        _timeline?.SetCrop(CropDrag.Drag(_cropGrabStart, _cropGrab, dx, dy));
    }

    private static Cursor? CropCursor(CropEdges edges) => edges switch
    {
        CropEdges.All => Cursors.SizeAll,
        CropEdges.Left or CropEdges.Right => Cursors.SizeWE,
        CropEdges.Top or CropEdges.Bottom => Cursors.SizeNS,
        CropEdges.Left | CropEdges.Top or CropEdges.Right | CropEdges.Bottom => Cursors.SizeNWSE,
        CropEdges.Right | CropEdges.Top or CropEdges.Left | CropEdges.Bottom => Cursors.SizeNESW,
        _ => null,
    };

    private void OnStageBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CropLayer.Visibility == Visibility.Visible)
        {
            DrawCropRectangle(); // its lines keep their width on the screen
        }
    }

    // The rectangle kept over the whole recording: the rest dimmed, a thin border and a handle at each corner and side.
    private void DrawCropRectangle()
    {
        double width = Stage.Width, height = Stage.Height;
        VideoCrop crop = _timeline?.Crop ?? VideoCrop.None;
        var kept = new Rect(crop.Left * width, crop.Top * height, crop.Width * width, crop.Height * height);
        // The picture is shrunk or grown to fit: the lines are worked out in pixels of the screen, about 1.5 of them.
        double line = 1.5 * width / Math.Max(1, StageBox.ActualWidth > 0 ? StageBox.ActualWidth : width / 2);

        CropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, width, height)), new RectangleGeometry(kept));
        CropFrame.StrokeThickness = line;
        CropFrame.Width = kept.Width;
        CropFrame.Height = kept.Height;
        Canvas.SetLeft(CropFrame, kept.X);
        Canvas.SetTop(CropFrame, kept.Y);

        if (CropHandles.Children.Count == 0)
        {
            for (int i = 0; i < 8; i++)
            {
                CropHandles.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.White });
            }
        }
        double size = line * 6;
        double[] xs = [kept.Left, kept.Left + kept.Width / 2, kept.Right];
        double[] ys = [kept.Top, kept.Top + kept.Height / 2, kept.Bottom];
        int n = 0;
        foreach (double y in ys)
        {
            foreach (double x in xs)
            {
                if (x == xs[1] && y == ys[1])
                {
                    continue; // the middle is grabbed anywhere inside
                }
                var handle = (System.Windows.Shapes.Rectangle)CropHandles.Children[n++];
                handle.Width = handle.Height = size;
                Canvas.SetLeft(handle, x - size / 2);
                Canvas.SetTop(handle, y - size / 2);
            }
        }
    }



    // ---------- look: frame, pointer and zoom style together

    /// <summary>The frame, pointer and zoom style chosen now.</summary>
    internal VideoLook Look => new(_frame, _cursor, _zoomStyle);

    // A change made in a panel: once the video is open it goes through the edit, so it can be undone (a slider's drag as
    // one step); the panel shows it itself.
    private void ChangeLook(VideoLook look)
    {
        look = look.Tidied();
        if (_timeline == null)
        {
            TakeLook(look, fromControls: true);
            return;
        }

        _lookFromControls = true;
        try
        {
            _timeline.SetLook(look); // comes back through OnTimelineChanged
        }
        finally
        {
            _lookFromControls = false;
        }
    }

    // Takes a look: remembered for the next video (written a moment after the last change), and shown. One that comes from
    // undo, redo or a preset is shown in the panels too; a panel's own change is left to it, so a slider is not disturbed
    // while it is dragged. The pointer and the scene are drawn again once per frame.
    private void TakeLook(VideoLook look, bool fromControls)
    {
        bool pointer = look.Cursor != _cursor;
        bool scene = look.Frame != _frame || look.Zoom != _zoomStyle;
        (_frame, _cursor, _zoomStyle) = (look.Frame, look.Cursor, look.Zoom);
        if (_settings != null)
        {
            _settings.Current.VideoLook = look;
            _frameSave.Stop();
            _frameSave.Start();
        }
        if (!fromControls)
        {
            ShowFrameChoices();
            ShowCursorChoices();
            ShowZoomStyle();
        }
        if (pointer)
        {
            SchedulePointer();
        }
        if (scene)
        {
            ScheduleScene();
        }
        if (PresetsPopup.IsOpen)
        {
            BuildPresetRows(); // the preset that matches is marked; a closed list is built when it opens
        }
    }

    private void ShowZoomStyle()
    {
        _showingZoomStyle = true;
        ZoomStyleDepthSlider.Value = _zoomStyle.Depth;
        ZoomSmoothTile.IsChecked = _zoomStyle.Motion == ZoomMotion.Smooth;
        ZoomFocusedTile.IsChecked = _zoomStyle.Motion == ZoomMotion.Focused;
        ShowZoomStyleValues();
        _showingZoomStyle = false;
    }

    private void ShowZoomStyleValues()
    {
        ZoomStyleDepthText.Text = $"{_zoomStyle.Depth.ToString("0.0#", CultureInfo.CurrentCulture)}×";
        ZoomStyleResetButton.IsEnabled = _zoomStyle != ZoomStyle.Default;
    }

    private void OnZoomStyleChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _showingZoomStyle)
        {
            return;
        }

        var style = new ZoomStyle(
            Math.Round(ZoomStyleDepthSlider.Value * 20) / 20,
            ZoomFocusedTile.IsChecked == true ? ZoomMotion.Focused : ZoomMotion.Smooth);
        ChangeLook(Look with { Zoom = style });
        if (sender is Slider)
        {
            ShowZoomStyleValues(); // a slider is left alone while it is dragged
        }
        else
        {
            ShowZoomStyle();
        }
    }

    private void OnZoomStyleSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => OnZoomStyleChanged(sender, e);

    private void OnZoomStyleReset(object sender, RoutedEventArgs e)
    {
        ChangeLook(Look with { Zoom = ZoomStyle.Default });
        ShowZoomStyle();
    }

    // ---------- presets

    /// <summary>The presets saved, in the order they were made.</summary>
    internal IReadOnlyList<VideoPreset> Presets => _presets;

    // The list opens under its button. A click on the button while the list is open closes it first (it closes on any click
    // outside it), and that click must not open it again.
    private void OnPresets(object sender, RoutedEventArgs e)
    {
        if (Environment.TickCount64 - _presetsClosedAt < 250)
        {
            return;
        }
        PresetNote.Text = "";
        BuildPresetRows();
        PresetsPopup.IsOpen = true;
    }

    private void OnPresetsClosed(object? sender, EventArgs e) => _presetsClosedAt = Environment.TickCount64;

    /// <summary>Gives the frame, pointer and zoom style of a preset, as one step to undo. False when there is none of that name.</summary>
    internal bool ApplyPreset(string name)
    {
        if (VideoPresetList.Find(_presets, name) is not { } preset || _busy)
        {
            return false;
        }

        if (_timeline != null)
        {
            _timeline.SetLook(preset.Look);
        }
        else
        {
            TakeLook(preset.Look.Tidied(), fromControls: false);
        }
        DeliveryText.Text = _timeline != null ? $"Preset «{preset.Name}» aplicado · Ctrl+Z lo deshace" : $"Preset «{preset.Name}» aplicado";
        return true;
    }

    /// <summary>Saves the look of now as <paramref name="name"/>, in place of a preset of that name. False when the name is empty.</summary>
    internal bool SavePreset(string name)
    {
        bool replacing = VideoPresetList.Find(_presets, name) != null;
        if (VideoPresetList.Save(_presets, name, Look) is not { } list)
        {
            PresetNote.Text = VideoPreset.TidyName(name) == null ? "Escribe un nombre para el preset." : "No caben más presets: borra alguno.";
            return false;
        }

        KeepPresets(list);
        string saved = VideoPresetList.Find(list, name)!.Name;
        PresetNote.Text = replacing ? $"«{saved}» se actualizó con el estilo actual." : $"«{saved}» guardado.";
        return true;
    }

    internal bool RenamePreset(string name, string newName)
    {
        if (VideoPresetList.Rename(_presets, name, newName) is not { } list)
        {
            PresetNote.Text = VideoPreset.TidyName(newName) == null ? "El nombre no puede quedar vacío." : "Ya hay un preset con ese nombre.";
            return false;
        }
        KeepPresets(list);
        return true;
    }

    internal bool DeletePreset(string name)
    {
        if (VideoPresetList.Delete(_presets, name) is not { } list)
        {
            return false;
        }
        KeepPresets(list);
        PresetNote.Text = $"«{VideoPreset.TidyName(name)}» borrado.";
        return true;
    }

    // In the settings at once: a preset is something the person meant to keep.
    private void KeepPresets(IReadOnlyList<VideoPreset> list)
    {
        _presets = list;
        if (_settings != null)
        {
            _settings.Current.Presets = list;
            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PresetNote.Text = $"No se pudieron guardar los presets: {ex.Message}";
            }
        }
        BuildPresetRows();
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        if (SavePreset(PresetNameBox.Text))
        {
            PresetNameBox.Text = "";
        }
    }

    private void OnPresetNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSavePreset(sender, e);
            e.Handled = true;
        }
    }

    // A row per preset: its name applies it (the one that matches the look of now is ticked), then rename and delete.
    // Renaming turns the name into a box; deleting asks once more on the same spot instead of opening a dialog.
    private void BuildPresetRows()
    {
        PresetRows.Children.Clear();
        PresetsEmptyText.Visibility = _presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VideoLook now = Look;
        foreach (VideoPreset preset in _presets)
        {
            string name = preset.Name;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bool current = preset.Look == now;
            var check = new ShapePath
            {
                Style = (Style)FindResource("Glyph"),
                Data = (Geometry)FindResource("CheckGlyph"),
                Visibility = current ? Visibility.Visible : Visibility.Hidden,
                Margin = new Thickness(0, 0, 6, 0),
            };
            check.SetResourceReference(Shape.StrokeProperty, "Accent"); // the theme's, whenever it is there
            var apply = new Button
            {
                Style = (Style)FindResource("PresetRow"),
                ToolTip = current
                    ? "Es el estilo de ahora. Aplicarlo de nuevo no cambia nada"
                    : "Aplicar: cambia el marco, el cursor y el estilo del zoom (Ctrl+Z lo deshace)",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        check,
                        new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 },
                    },
                },
            };
            apply.Click += (_, _) =>
            {
                if (ApplyPreset(name))
                {
                    PresetsPopup.IsOpen = false;
                }
            };
            row.Children.Add(apply);

            var rename = new Button { Style = (Style)FindResource("IconButton"), ToolTip = "Cambiar el nombre" };
            rename.Content = new ShapePath { Style = (Style)FindResource("Glyph"), Data = (Geometry)FindResource("RenameGlyph") };
            rename.Click += (_, _) => StartRenaming(row, apply, name);
            Grid.SetColumn(rename, 1);
            row.Children.Add(rename);

            var delete = new Button { Style = (Style)FindResource("IconButton"), ToolTip = "Borrar este preset" };
            delete.Content = new ShapePath { Style = (Style)FindResource("Glyph"), Data = (Geometry)FindResource("TrashGlyph") };
            delete.Click += (_, _) =>
            {
                var sure = new Button { Content = "¿Borrar?", Style = (Style)FindResource("LinkButton"), Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Haz clic otra vez para borrarlo" };
                sure.Click += (_, _) => DeletePreset(name);
                Grid.SetColumn(sure, 2);
                row.Children.Remove(delete);
                row.Children.Add(sure);
            };
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            PresetRows.Children.Add(row);
        }
    }

    private void StartRenaming(Grid row, Button apply, string name)
    {
        var box = new TextBox { Text = name, MaxLength = VideoPreset.MaxNameLength, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 4, 2) };
        bool done = false;
        void Finish(bool keep)
        {
            if (done)
            {
                return;
            }
            done = true;
            if (!keep || box.Text == name || RenamePreset(name, box.Text))
            {
                BuildPresetRows();
            }
            else
            {
                done = false; // refused: the box stays, with the reason under the list
                box.SelectAll();
            }
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(keep: true);
        row.Children.Remove(apply);
        row.Children.Add(box);
        box.Focus();
        box.SelectAll();
    }

    // ---------- pointer

    // The side column shows one panel at a time, the frame's or the pointer's.
    private void OnPanelTab(object sender, RoutedEventArgs e)
    {
        if (FramePanel == null || CursorPanel == null || ZoomPanel == null || CropPanel == null)
        {
            return; // the first tab is checked while the XAML loads
        }
        FramePanel.Visibility = FrameTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CursorPanel.Visibility = CursorTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ZoomPanel.Visibility = ZoomTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CropPanel.Visibility = CropTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // Opening the crop shows the whole recording to choose on; leaving it shows the result.
        bool cropping = CropTab.IsChecked == true;
        if (cropping && Lane.SelectedZoom >= 0)
        {
            Lane.SelectedZoom = -1;
        }
        if (cropping != (CropLayer.Visibility == Visibility.Visible))
        {
            ApplyFrame();
            ApplyZoomPreview();
        }
        Stage.Cursor = null;
    }

    /// <summary>The pointer drawn on the video, as chosen now.</summary>
    internal CursorStyle PointerStyle => _cursor;

    // A tile for each shape, with a drawing of it.
    private void BuildCursorChoices()
    {
        foreach (CursorShape shape in Enum.GetValues<CursorShape>())
        {
            (Color fill, Color edge) = CursorArt.Paint(shape);
            Geometry geometry = CursorArt.Geometry(shape);
            var drawing = new Canvas
            {
                Width = 0,
                Height = 0,
                // The arrow hangs from its tip; the dot is round its centre.
                Margin = shape == CursorShape.Dot ? new Thickness(0) : new Thickness(-14, -22, 0, 0),
                RenderTransform = new ScaleTransform(1.25, 1.25),
                Children =
                {
                    new ShapePath { Data = geometry, Fill = new SolidColorBrush(edge), Stroke = new SolidColorBrush(edge), StrokeThickness = CursorArt.EdgeWidth * 2, StrokeLineJoin = PenLineJoin.Round },
                    new ShapePath { Data = geometry, Fill = new SolidColorBrush(fill) },
                },
            };
            var tile = new RadioButton
            {
                Style = (Style)FindResource("Tile"),
                GroupName = "cursorShape",
                Tag = shape,
                Content = drawing,
                ToolTip = shape switch
                {
                    CursorShape.LightArrow => "Flecha blanca, como la de Windows",
                    CursorShape.Dot => "Un punto: marca dónde está sin tapar nada",
                    _ => "Flecha negra con borde blanco: se ve sobre ventanas claras y oscuras",
                },
            };
            tile.Checked += OnCursorChanged;
            CursorShapes.Children.Add(tile);
        }

        // The click effects, each with a drawing of it in the colour it has in the video.
        var blue = new SolidColorBrush(CursorArt.ClickColor);
        foreach (ClickEffect effect in Enum.GetValues<ClickEffect>())
        {
            ShapePath icon = effect switch
            {
                ClickEffect.None => new ShapePath
                {
                    Data = Geometry.Parse("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M6.3,6.3 L17.7,17.7"),
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)),
                    StrokeThickness = 1.8,
                },
                ClickEffect.Pulse => new ShapePath
                {
                    Data = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(0x90, CursorArt.ClickColor.R, CursorArt.ClickColor.G, CursorArt.ClickColor.B)),
                },
                _ => new ShapePath
                {
                    Data = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z"),
                    Stroke = blue,
                    StrokeThickness = 1.8,
                },
            };
            icon.Width = icon.Height = 24;
            var tile = new RadioButton
            {
                Style = (Style)FindResource("Tile"),
                GroupName = "cursorClick",
                Tag = effect,
                Content = icon,
                ToolTip = effect switch
                {
                    ClickEffect.None => "Sin efecto en los clics",
                    ClickEffect.Pulse => "Pulso: una mancha suave que crece y se apaga",
                    _ => "Onda: un anillo que crece desde el puntero",
                },
            };
            tile.Checked += OnCursorChanged;
            CursorClicks.Children.Add(tile);
        }
        ShowCursorChoices();
    }

    private void ShowCursorChoices()
    {
        _showingCursor = true;
        CursorBox.IsChecked = _cursor.Visible;
        foreach (RadioButton tile in CursorShapes.Children.OfType<RadioButton>())
        {
            tile.IsChecked = (CursorShape)tile.Tag == _cursor.Shape;
        }
        CursorSizeSlider.Value = _cursor.Size;
        CursorSmoothingSlider.Value = _cursor.Smoothing;
        CursorSwaySlider.Value = _cursor.Sway;
        foreach (RadioButton tile in CursorClicks.Children.OfType<RadioButton>())
        {
            tile.IsChecked = (ClickEffect)tile.Tag == _cursor.Click;
        }
        CursorResetButton.IsEnabled = _cursor with { Visible = true } != CursorStyle.Default with { Visible = true };
        CursorBounceBox.IsChecked = _cursor.Bounce;
        ShowCursorValues();

        // Only a video recorded without the pointer, with its track, can have one drawn.
        string? why = _timeline == null ? null
            : _track == null ? "Este video no guardó el recorrido del cursor: se grabó con una versión anterior de Zarpazo."
            : _track.PointerInPicture ? "Este video ya tiene el cursor dentro de la imagen: se grabó con una versión anterior de Zarpazo."
            : null;
        bool available = _timeline == null || why == null;
        CursorNote.Text = why ?? "";
        CursorNote.Visibility = why == null ? Visibility.Collapsed : Visibility.Visible;
        CursorBox.IsEnabled = available;
        CursorOptions.IsEnabled = available && _cursor.Visible;
        CursorOptions.Opacity = CursorOptions.IsEnabled ? 1 : 0.5;
        _showingCursor = false;
    }

    private void ShowCursorValues()
    {
        CursorSizeText.Text = $"{_cursor.Size.ToString("0.0", CultureInfo.CurrentCulture)}×";
        CursorSmoothingText.Text = $"{Math.Round(_cursor.Smoothing * 100)} %";
        CursorSwayText.Text = $"{Math.Round(_cursor.Sway * 100)} %";
    }

    private void OnCursorSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => OnCursorChanged(sender, e);

    private void OnCursorChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _showingCursor)
        {
            return;
        }

        CursorShape shape = CursorShapes.Children.OfType<RadioButton>().FirstOrDefault(tile => tile.IsChecked == true)?.Tag as CursorShape? ?? _cursor.Shape;
        ChangeLook(Look with { Cursor = new CursorStyle(
            CursorBox.IsChecked == true,
            shape,
            Math.Round(CursorSizeSlider.Value, 1),
            Math.Round(CursorSmoothingSlider.Value * 20) / 20,
            Math.Round(CursorSwaySlider.Value * 20) / 20,
            CursorClicks.Children.OfType<RadioButton>().FirstOrDefault(tile => tile.IsChecked == true)?.Tag as ClickEffect? ?? _cursor.Click,
            CursorBounceBox.IsChecked == true).Tidied() });
        if (sender is not Slider)
        {
            ShowCursorChoices(); // a slider is left alone while it is dragged
        }
        else
        {
            ShowCursorValues();
            CursorResetButton.IsEnabled = true;
        }
    }

    private void OnCursorReset(object sender, RoutedEventArgs e)
    {
        ChangeLook(Look with { Cursor = CursorStyle.Default with { Visible = _cursor.Visible } });
        ShowCursorChoices();
    }

    // A slider sends a change for every pixel it moves: the pointer is drawn again once per frame, with the latest style.
    private void SchedulePointer()
    {
        if (_pointerPending)
        {
            return;
        }
        _pointerPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _pointerPending = false;
            RebuildPointer();
            ApplyZoomPreview();
            RefreshState();
        });
    }

    private void SaveCursorChoice()
    {
        _frameSave.Stop();
        _settings?.Save();
    }

    // The pointer's path for the style chosen, and its drawing.
    private void RebuildPointer()
    {
        _motion = _track is not { PointerInPicture: false } ? null
            : _motion?.Restyled(_track, _cursor) ?? new CursorMotion(_track, _cursor);
        UpdatePointerVisibility();
        (Color fill, Color edge) = CursorArt.Paint(_cursor.Shape);
        Geometry geometry = CursorArt.Geometry(_cursor.Shape);
        PointerEdge.Data = PointerFill.Data = geometry;
        PointerEdge.Fill = PointerEdge.Stroke = new SolidColorBrush(edge);
        PointerEdge.StrokeThickness = CursorArt.EdgeWidth * 2;
        PointerFill.Fill = new SolidColorBrush(fill);
    }

    // The drawn pointer shows when there is one to draw, and not over the whole recording while the crop is chosen.
    private void UpdatePointerVisibility() =>
        PointerLayer.Visibility = _motion != null && _cursor.Visible && !CropEditing ? Visibility.Visible : Visibility.Collapsed;

    // Puts the pointer and the clicks where the saved video will have them at this moment of the edited video.
    private void UpdatePointer(double time, ZoomView view)
    {
        if (_motion == null || _timeline == null || PointerLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        double width = Stage.Width, height = Stage.Height;
        double original = CursorLayer.ToOriginal(_timeline.Segments, time);
        double unit = CursorLayer.Unit(_cursor, _track!.PointerScale > 0 ? _track.PointerScale : 1, view, _layout);

        IReadOnlyList<ClickMark> clicks = _motion.ClicksAt(original);
        while (_clickMarks.Count < clicks.Count)
        {
            var mark = new Ellipse();
            _clickMarks.Add(mark);
            ClickMarks.Children.Add(mark);
        }
        for (int i = 0; i < _clickMarks.Count; i++)
        {
            Ellipse mark = _clickMarks[i];
            if (i >= clicks.Count)
            {
                mark.Visibility = Visibility.Collapsed;
                continue;
            }

            ClickMark click = clicks[i];
            if (CursorLayer.Shown(click.X, click.Y, _area) is not { } clickAt)
            {
                mark.Visibility = Visibility.Collapsed; // in the part cut away
                continue;
            }
            double growth = CursorArt.ClickGrowth(click.Progress);
            byte alpha = (byte)Math.Round(255 * CursorArt.ClickOpacity(click.Progress));
            Color color = CursorArt.ClickColor;
            double radius;
            if (_cursor.Click == ClickEffect.Pulse)
            {
                radius = (CursorArt.PulseStartRadius + (CursorArt.PulseEndRadius - CursorArt.PulseStartRadius) * growth) * unit;
                mark.Fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.45), color.R, color.G, color.B));
                mark.Stroke = null;
            }
            else
            {
                radius = (CursorArt.RippleStartRadius + (CursorArt.RippleEndRadius - CursorArt.RippleStartRadius) * growth) * unit;
                mark.Fill = null;
                mark.Stroke = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                mark.StrokeThickness = CursorArt.RippleWidth * unit;
            }
            // GDI+ draws the ring's line centred on the circle, WPF inside the ellipse: half a line wider matches them.
            double outer = radius + (mark.Stroke != null ? mark.StrokeThickness / 2 : 0);
            (double x, double y) = CursorLayer.ToPicture(clickAt.X, clickAt.Y, view, _layout, width, height);
            mark.Width = mark.Height = outer * 2;
            Canvas.SetLeft(mark, x - outer);
            Canvas.SetTop(mark, y - outer);
            mark.Visibility = Visibility.Visible;
        }

        if (_motion.PoseAt(original) is not { } pose || CursorLayer.Shown(pose.X, pose.Y, _area) is not { } tip)
        {
            PointerGlyph.Visibility = Visibility.Collapsed; // none yet, or in the part cut away
            return;
        }

        (double tipX, double tipY) = CursorLayer.ToPicture(tip.X, tip.Y, view, _layout, width, height);
        var matrix = Matrix.Identity;
        matrix.Scale(unit * pose.Scale, unit * pose.Scale);
        matrix.Rotate(pose.Angle);
        matrix.Translate(tipX, tipY);
        PointerGlyph.RenderTransform = new MatrixTransform(matrix);
        PointerGlyph.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true; // the file is being written
            return;
        }

        // Only what the person changed asks first (the automatic zooms alone are not worth a question), and not once the
        // video with those changes was copied or saved.
        if (_timeline?.HasChanges == true && !_delivery.IsCurrent(Recipe))
        {
            MessageBoxResult answer = MessageBox.Show(this, "Hay cambios que no copiaste ni guardaste. ¿Quieres cerrar igual?", "Zarpazo",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            e.Cancel = answer != MessageBoxResult.Yes;
        }
    }
}
