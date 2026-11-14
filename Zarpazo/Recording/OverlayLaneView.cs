using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>
/// The row under the zooms with the blurs, texts and arrows, each a block where it shows in the edited video; blocks that
/// overlap in time go on rows of their own. Click a block to choose it, drag it to move it, drag its sides to make it longer
/// or shorter. It lines up with <see cref="TimelineView"/> above it: same scale, same padding.
/// </summary>
internal sealed class OverlayLaneView : FrameworkElement
{
    public const double RowPitch = 26;
    public const double EmptyHeight = 34;
    private const double Top = 4;
    private const double BlockHeight = 22;
    private const double EdgeWidth = 7;
    private const double DragThreshold = 3;

    private static readonly Brush BlurFill = Frozen(Color.FromArgb(0x80, 0x5B, 0x8D, 0xD9));
    private static readonly Brush TextFill = Frozen(Color.FromArgb(0x80, 0xE0, 0xA1, 0x2E));
    private static readonly Brush ArrowFill = Frozen(Color.FromArgb(0x80, 0xE5, 0x48, 0x4D));

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
    private IReadOnlyList<VideoSegment>? _seenSegments;
    private IReadOnlyList<VideoOverlay>? _seenOverlays;
    private IReadOnlyList<OverlaySpan> _spans = [];
    private IReadOnlyList<int> _rows = [];
    private int _rowCount = 1;
    private double _pixelsPerSecond = 20;
    private double _playhead;
    private int _selected = -1;
    private Gesture _gesture;
    private int _gestureOverlay;
    private double _grabX;
    private VideoOverlay _original;
    private bool _menuAllowed;

    public OverlayLaneView()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        ContextMenuOpening += (_, e) => e.Handled = !_menuAllowed; // the menu only acts on an overlay
    }

    /// <summary>The user chose an overlay (its index in <see cref="EditTimeline.Overlays"/>), or none (-1).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user clicked on the row: the moment of the edited video, to move the playhead there.</summary>
    public event EventHandler<double>? Scrubbed;

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

    /// <summary>The overlay chosen, as an index into <see cref="EditTimeline.Overlays"/>; -1 for none.</summary>
    public int SelectedOverlay
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

    /// <summary>
    /// The row each block goes on: the first one where it does not touch a block already there, so overlays at the same
    /// moment sit one under the other. Returns one number per span, from 0.
    /// </summary>
    public static IReadOnlyList<int> Rows(IReadOnlyList<OverlaySpan> spans)
    {
        var rows = new int[spans.Count];
        var ends = new List<double>(); // where the last block of each row ends
        foreach (int i in Enumerable.Range(0, spans.Count).OrderBy(i => spans[i].Start))
        {
            int row = ends.FindIndex(end => end <= spans[i].Start + 0.0001);
            if (row < 0)
            {
                ends.Add(0);
                row = ends.Count - 1;
            }
            ends[row] = spans[i].End;
            rows[i] = row;
        }
        return rows;
    }

    /// <summary>The height the row needs for this many rows of blocks.</summary>
    public static double HeightFor(int rows) => Math.Max(EmptyHeight, Top * 2 + rows * RowPitch - (RowPitch - BlockHeight));

    /// <summary>The block and the part of it under a point of the row: which span (index into <paramref name="spans"/>) and where.</summary>
    public static (Part Part, int Span) HitTest(IReadOnlyList<OverlaySpan> spans, IReadOnlyList<int> rows, double pixelsPerSecond, Point point)
    {
        // Later blocks are drawn over earlier ones, so they are looked at first.
        for (int i = spans.Count - 1; i >= 0; i--)
        {
            double blockTop = Top + rows[i] * RowPitch;
            if (point.Y < blockTop || point.Y > blockTop + BlockHeight)
            {
                continue;
            }

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
            return right - point.X < edge ? (Part.EndEdge, i) : (Part.Body, i);
        }
        return (Part.None, -1);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(_timeline == null ? 0 : TimelineLayout.Width(_timeline, _pixelsPerSecond), HeightFor(_rowCount));

    // A new crop or look leaves the pieces and the overlays as they were (the same lists): nothing to work out or draw again.
    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_timeline?.Segments, _seenSegments) && ReferenceEquals(_timeline?.Overlays, _seenOverlays))
        {
            return;
        }
        Refresh();
    }

    private void Refresh()
    {
        _seenSegments = _timeline?.Segments;
        _seenOverlays = _timeline?.Overlays;
        _spans = _timeline == null ? [] : OverlaySpan.Place(_timeline.Segments, _timeline.Overlays);
        _rows = Rows(_spans);
        _rowCount = Math.Max(1, _rows.Count == 0 ? 1 : _rows.Max() + 1);
        if (_timeline != null && _selected >= _timeline.Overlays.Count)
        {
            SelectedOverlay = -1;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ---------- mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_timeline == null)
        {
            return;
        }

        Point point = e.GetPosition(this);
        (Part part, int span) = HitTest(_spans, _rows, _pixelsPerSecond, point);
        double time = Math.Clamp(TimelineLayout.TimeAt(point.X, _pixelsPerSecond), 0, _timeline.Duration);
        if (part == Part.None)
        {
            SelectedOverlay = -1;
            Scrubbed?.Invoke(this, time);
            e.Handled = true;
            return;
        }

        _gestureOverlay = _spans[span].Index;
        _original = _timeline.Overlays[_gestureOverlay];
        _grabX = point.X;
        _gesture = part switch
        {
            Part.StartEdge => Gesture.Start,
            Part.EndEdge => Gesture.End,
            _ => Gesture.Click, // becomes a move once the pointer has gone a few pixels
        };
        SelectedOverlay = _gestureOverlay;
        if (_gesture != Gesture.Click)
        {
            _timeline.BeginGesture();
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_timeline == null)
        {
            return;
        }

        Point point = e.GetPosition(this);
        if (_gesture == Gesture.None)
        {
            Cursor = HitTest(_spans, _rows, _pixelsPerSecond, point).Part switch
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

        double delta = (point.X - _grabX) / _pixelsPerSecond;
        switch (_gesture)
        {
            case Gesture.Move:
                _timeline.MoveOverlay(_gestureOverlay, _original.Start + delta);
                break;
            case Gesture.Start:
                _timeline.SetOverlayStart(_gestureOverlay, _original.Start + delta);
                break;
            case Gesture.End:
                _timeline.SetOverlayEnd(_gestureOverlay, _original.End + delta);
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
            // A plain click on a block also moves the playhead there, so the overlay shows in the player.
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
        if (IsDragging)
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

    // The right button chooses the block under the pointer, so the menu that follows acts on it.
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _menuAllowed = false;
        (Part part, int span) = HitTest(_spans, _rows, _pixelsPerSecond, e.GetPosition(this));
        if (part != Part.None)
        {
            SelectedOverlay = _spans[span].Index;
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
        if (_timeline == null)
        {
            return;
        }

        Brush border = TryFindResource("Border") as Brush ?? Brushes.Gray;
        Brush accent = TryFindResource("Accent") as Brush ?? Brushes.IndianRed;
        Brush secondary = TryFindResource("TextSecondary") as Brush ?? Brushes.Silver;
        Brush primary = TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var semibold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        double right = TimelineLayout.XOf(_timeline.Duration, _pixelsPerSecond);
        var lane = new Rect(TimelineLayout.Padding, Top, Math.Max(0, right - TimelineLayout.Padding), ActualHeight - Top * 2);
        dc.DrawRoundedRectangle(null, new Pen(border, 1) { DashStyle = DashStyles.Dash }, lane, 6, 6);

        if (_spans.Count == 0)
        {
            var hint = new FormattedText("Desenfoques, textos y flechas: elige una herramienta en la pestaña de la izquierda",
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, secondary, pixelsPerDip);
            dc.DrawText(hint, new Point(lane.Left + 10, lane.Top + (lane.Height - hint.Height) / 2));
        }

        for (int i = 0; i < _spans.Count; i++)
        {
            OverlaySpan span = _spans[i];
            VideoOverlay overlay = _timeline.Overlays[span.Index];
            double left = TimelineLayout.XOf(span.Start, _pixelsPerSecond);
            double width = Math.Max(4, TimelineLayout.XOf(span.End, _pixelsPerSecond) - left);
            var block = new Rect(left, Top + _rows[i] * RowPitch, width, BlockHeight);
            bool selected = span.Index == _selected;
            Brush fill = overlay.Kind switch { OverlayKind.Blur => BlurFill, OverlayKind.Text => TextFill, _ => ArrowFill };
            dc.DrawRoundedRectangle(fill, new Pen(selected ? accent : Brushes.Transparent, selected ? 2 : 0), block, 6, 6);

            var label = new FormattedText(Label(overlay), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, semibold, 12, primary, pixelsPerDip);
            if (label.Width + 16 <= width)
            {
                dc.PushClip(new RectangleGeometry(block));
                dc.DrawText(label, new Point(left + 8, block.Top + (BlockHeight - label.Height) / 2));
                dc.Pop();
            }
        }

        double x = Math.Round(TimelineLayout.XOf(Math.Clamp(_playhead, 0, _timeline.Duration), _pixelsPerSecond)) + 0.5;
        dc.DrawLine(new Pen(accent, 2), new Point(x, 0), new Point(x, ActualHeight));
    }

    /// <summary>The words a block carries on the row.</summary>
    public static string Label(VideoOverlay overlay) => overlay.Kind switch
    {
        OverlayKind.Blur => "Desenfoque",
        OverlayKind.Arrow => "Flecha",
        _ => string.IsNullOrWhiteSpace(overlay.Text) ? "Texto" : overlay.Text.Replace('\n', ' '),
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
