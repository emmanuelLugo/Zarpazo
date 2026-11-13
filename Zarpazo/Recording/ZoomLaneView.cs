using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>
/// The row under the timeline with the zooms, each a block where it plays in the edited video. Click a block to choose it,
/// drag it to move it, drag its sides to make it longer or shorter, double-click an empty place to add one. It lines up with
/// <see cref="TimelineView"/> above it: same scale, same padding.
/// </summary>
internal sealed class ZoomLaneView : FrameworkElement
{
    public const double LaneHeight = 34;
    private const double BlockTop = 4;
    private const double BlockHeight = 26;
    private const double EdgeWidth = 7;
    private const double DragThreshold = 3;

    private static readonly Brush BlockFill = Frozen(Color.FromArgb(0x70, 0x8E, 0x6C, 0xEF));
    private static readonly Brush BlockEdge = Frozen(Color.FromRgb(0x8E, 0x6C, 0xEF));

    /// <summary>What a point of the row is over.</summary>
    public enum Part
    {
        None,
        Body,
        StartEdge,
        EndEdge,
    }

    private enum Gesture
    {
        None,
        Click,
        Move,
        Start,
        End,
    }

    private EditTimeline? _timeline;
    private IReadOnlyList<VideoSegment>? _seenSegments; // what the lane was last worked out for
    private IReadOnlyList<ZoomRegion>? _seenZooms;
    private ZoomCamera? _camera;
    private double _pixelsPerSecond = 20;
    private double _playhead;
    private int _selected = -1;
    private Gesture _gesture;
    private int _gestureZoom;
    private double _grabX;
    private ZoomRegion _original;
    private bool _moved;

    private bool _menuAllowed;

    public ZoomLaneView()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        ContextMenuOpening += (_, e) => e.Handled = !_menuAllowed; // the menu only acts on a zoom, so none over an empty place
    }

    /// <summary>The user chose a zoom (its index in <see cref="EditTimeline.Zooms"/>), or none (-1).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user clicked somewhere on the row: the moment of the edited video, to move the playhead there.</summary>
    public event EventHandler<double>? Scrubbed;

    /// <summary>The user double-clicked an empty place: add a zoom at this moment of the edited video.</summary>
    public event EventHandler<double>? AddRequested;

    /// <summary>A drag ended.</summary>
    public event EventHandler? GestureFinished;

    /// <summary>The user turned the wheel with Ctrl held (x of the pointer, wheel delta), like on the timeline.</summary>
    public event EventHandler<(double X, int Delta)>? ZoomRequested;

    public EditTimeline? Timeline
    {
        get => _timeline;
        set
        {
            if (_timeline != null)
            {
                _timeline.Changed -= OnTimelineChanged;
            }
            _timeline = value;
            if (_timeline != null)
            {
                _timeline.Changed += OnTimelineChanged;
            }
            Refresh();
        }
    }

    /// <summary>Where the zooms fall in the edited video; rebuilt whenever the edit changes.</summary>
    public ZoomCamera? Camera => _camera;

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            value = Math.Max(0.5, value);
            if (Math.Abs(value - _pixelsPerSecond) >= 0.0001)
            {
                _pixelsPerSecond = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    public double Playhead
    {
        get => _playhead;
        set
        {
            _playhead = value;
            InvalidateVisual();
        }
    }

    /// <summary>The zoom chosen, as an index into <see cref="EditTimeline.Zooms"/>; -1 for none.</summary>
    public int SelectedZoom
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                InvalidateVisual();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>True while a block is being dragged.</summary>
    public bool IsDragging => _gesture is Gesture.Move or Gesture.Start or Gesture.End;

    /// <summary>The block and the part of it under a point of the row: which span (index into the camera's spans) and where.</summary>
    public static (Part Part, int Span) HitTest(IReadOnlyList<ZoomSpan> spans, double pixelsPerSecond, Point point)
    {
        if (point.Y < 0 || point.Y > LaneHeight)
        {
            return (Part.None, -1);
        }

        for (int i = 0; i < spans.Count; i++)
        {
            double left = TimelineLayout.XOf(spans[i].Start, pixelsPerSecond);
            double right = TimelineLayout.XOf(spans[i].End, pixelsPerSecond);
            if (point.X < left - EdgeWidth / 2 || point.X > right + EdgeWidth / 2)
            {
                continue;
            }

            double edge = Math.Min(EdgeWidth, (right - left) / 3);
            if (point.X - left < edge)
            {
                return (Part.StartEdge, i);
            }
            if (right - point.X < edge)
            {
                return (Part.EndEdge, i);
            }
            return (Part.Body, i);
        }
        return (Part.None, -1);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(_timeline == null ? 0 : TimelineLayout.Width(_timeline, _pixelsPerSecond), LaneHeight);

    // A new crop or look leaves the pieces and the zooms as they were (the same lists): nothing to work out or draw again.
    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_timeline?.Segments, _seenSegments) && ReferenceEquals(_timeline?.Zooms, _seenZooms))
        {
            return;
        }
        Refresh();
    }

    private void Refresh()
    {
        _seenSegments = _timeline?.Segments;
        _seenZooms = _timeline?.Zooms;
        _camera = _timeline == null ? null : new ZoomCamera(_timeline.Segments, _timeline.Zooms);
        if (_timeline != null && _selected >= _timeline.Zooms.Count)
        {
            SelectedZoom = -1;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ---------- mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_timeline == null || _camera == null)
        {
            return;
        }

        Point point = e.GetPosition(this);
        (Part part, int span) = HitTest(_camera.Spans, _pixelsPerSecond, point);
        if (part == Part.None)
        {
            SelectedZoom = -1;
            if (e.ClickCount == 2)
            {
                AddRequested?.Invoke(this, Math.Clamp(TimelineLayout.TimeAt(point.X, _pixelsPerSecond), 0, _timeline.Duration));
            }
            else
            {
                Scrubbed?.Invoke(this, Math.Clamp(TimelineLayout.TimeAt(point.X, _pixelsPerSecond), 0, _timeline.Duration));
            }
            e.Handled = true;
            return;
        }

        _gestureZoom = _camera.Spans[span].Index;
        _original = _timeline.Zooms[_gestureZoom];
        _grabX = point.X;
        _moved = false;
        _gesture = part switch
        {
            Part.StartEdge => Gesture.Start,
            Part.EndEdge => Gesture.End,
            _ => Gesture.Click, // becomes a move once the pointer has gone a few pixels
        };
        SelectedZoom = _gestureZoom;
        if (_gesture != Gesture.Click)
        {
            _timeline.BeginGesture();
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_timeline == null || _camera == null)
        {
            return;
        }

        Point point = e.GetPosition(this);
        if (_gesture == Gesture.None)
        {
            (Part part, _) = HitTest(_camera.Spans, _pixelsPerSecond, point);
            Cursor = part switch
            {
                Part.StartEdge or Part.EndEdge => Cursors.SizeWE,
                Part.Body => Cursors.SizeAll,
                _ => Cursors.Arrow,
            };
            return;
        }

        if (_gesture == Gesture.Click && Math.Abs(point.X - _grabX) >= DragThreshold)
        {
            _gesture = Gesture.Move;
            _timeline.BeginGesture();
        }

        // Seconds of the edited video and of the original are the same length, so the drag moves the zoom by as much.
        double delta = (point.X - _grabX) / _pixelsPerSecond;
        switch (_gesture)
        {
            case Gesture.Move:
                _moved |= _timeline.MoveZoom(_gestureZoom, _original.Start + delta);
                break;
            case Gesture.Start:
                _moved |= _timeline.SetZoomStart(_gestureZoom, _original.Start + delta);
                break;
            case Gesture.End:
                _moved |= _timeline.SetZoomEnd(_gestureZoom, _original.End + delta);
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_gesture == Gesture.None)
        {
            return;
        }

        Gesture gesture = _gesture;
        Finish();
        if (gesture == Gesture.Click && _timeline != null)
        {
            // A plain click on a block also moves the playhead there, so the zoom shows in the player.
            Scrubbed?.Invoke(this, Math.Clamp(TimelineLayout.TimeAt(e.GetPosition(this).X, _pixelsPerSecond), 0, _timeline.Duration));
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_gesture != Gesture.None)
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (_gesture is Gesture.Move or Gesture.Start or Gesture.End)
        {
            _timeline?.EndGesture();
        }
        _gesture = Gesture.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        GestureFinished?.Invoke(this, EventArgs.Empty);
    }

    // The right button chooses the zoom under the pointer, so the menu that follows acts on it.
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _menuAllowed = false;
        if (_camera == null)
        {
            return;
        }

        (Part part, int span) = HitTest(_camera.Spans, _pixelsPerSecond, e.GetPosition(this));
        if (part != Part.None)
        {
            SelectedZoom = _camera.Spans[span].Index;
            _menuAllowed = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomRequested?.Invoke(this, (e.GetPosition(this).X, e.Delta));
            e.Handled = true;
        }
    }

    // ---------- drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // the empty parts take clicks too
        if (_timeline == null || _camera == null)
        {
            return;
        }

        Brush border = TryFindResource("Border") as Brush ?? Brushes.Gray;
        Brush accent = TryFindResource("Accent") as Brush ?? Brushes.IndianRed;
        Brush secondary = TryFindResource("TextSecondary") as Brush ?? Brushes.Silver;
        Brush primary = TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double right = TimelineLayout.XOf(_timeline.Duration, _pixelsPerSecond);
        var lane = new Rect(TimelineLayout.Padding, BlockTop, Math.Max(0, right - TimelineLayout.Padding), BlockHeight);
        dc.DrawRoundedRectangle(null, new Pen(border, 1) { DashStyle = DashStyles.Dash }, lane, 6, 6);

        if (_camera.IsEmpty)
        {
            var hint = new FormattedText("Zoom: doble clic aquí o pulsa Z para añadir uno donde está la línea roja",
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, secondary, pixelsPerDip);
            dc.DrawText(hint, new Point(lane.Left + 10, lane.Top + (lane.Height - hint.Height) / 2));
        }

        foreach (ZoomSpan span in _camera.Spans)
        {
            double left = TimelineLayout.XOf(span.Start, _pixelsPerSecond);
            double width = Math.Max(4, TimelineLayout.XOf(span.End, _pixelsPerSecond) - left);
            var block = new Rect(left, BlockTop, width, BlockHeight);
            bool selected = span.Index == _selected;
            dc.DrawRoundedRectangle(BlockFill, new Pen(selected ? accent : BlockEdge, selected ? 2 : 1), block, 6, 6);

            var label = new FormattedText($"Zoom {span.Scale.ToString("0.##", CultureInfo.CurrentCulture)}×",
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                12, primary, pixelsPerDip);
            if (label.Width + 16 <= width)
            {
                dc.PushClip(new RectangleGeometry(block));
                dc.DrawText(label, new Point(left + 8, BlockTop + (BlockHeight - label.Height) / 2));
                dc.Pop();
            }
        }

        double x = Math.Round(TimelineLayout.XOf(Math.Clamp(_playhead, 0, _timeline.Duration), _pixelsPerSecond)) + 0.5;
        dc.DrawLine(new Pen(accent, 2), new Point(x, 0), new Point(x, LaneHeight));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
