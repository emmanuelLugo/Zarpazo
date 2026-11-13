using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Recording;

/// <summary>
/// The timeline of the video editor: a ruler and, under it, every piece of the edit as a block with pictures from the
/// video. Click or drag on the ruler or a block's pictures to move the playhead; drag a block by its title to change the
/// order; drag its sides to change where it starts or ends. All the editing rules live in <see cref="EditTimeline"/>.
/// </summary>
internal sealed class TimelineView : FrameworkElement
{
    private enum Gesture
    {
        None,
        Scrub,
        TrimStart,
        TrimEnd,
        Reorder,
    }

    private const double ReorderThreshold = 4;

    private EditTimeline? _timeline;
    private IReadOnlyList<VideoSegment>? _drawnSegments; // the pieces last drawn, to skip changes that leave them alone
    private double _pixelsPerSecond = 20;
    private double _playhead;
    private int _selected;
    private BitmapSource?[] _thumbnails = Array.Empty<BitmapSource?>();

    private Gesture _gesture;
    private int _gestureIndex;
    private double _grabX;
    private double _pointerX;
    private double _originalStart;
    private double _originalEnd;
    private bool _reordering;
    private bool _menuAllowed;
    private string? _bubble;
    private TimelineHit _hover = new(TimelinePart.None, -1);

    public TimelineView()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        ContextMenuOpening += (_, e) => e.Handled = !_menuAllowed; // no menu with nothing under the pointer to act on
    }

    /// <summary>The user moved the playhead (output time).</summary>
    public event EventHandler<double>? Scrubbed;

    /// <summary>The user chose another piece.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user is dragging an edge; this is the moment of the original to show in the player (seconds).</summary>
    public event EventHandler<double>? Previewed;

    /// <summary>A drag ended, whatever it did.</summary>
    public event EventHandler? GestureFinished;

    /// <summary>The user turned the wheel with Ctrl held: zoom, anchored where the pointer is (x of the pointer, wheel delta).</summary>
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
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            value = Math.Max(0.5, value);
            if (Math.Abs(value - _pixelsPerSecond) < 0.0001)
            {
                return;
            }
            _pixelsPerSecond = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Where the video is, in seconds of the edited video.</summary>
    public double Playhead
    {
        get => _playhead;
        set
        {
            _playhead = value;
            InvalidateVisual();
        }
    }

    public double PlayheadX => TimelineLayout.XOf(Math.Clamp(_playhead, 0, _timeline?.Duration ?? 0), _pixelsPerSecond);

    /// <summary>The piece the buttons act on.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>True while a drag is changing the edit: the scale must not change under the pointer.</summary>
    public bool IsDragging => _gesture != Gesture.None;

    /// <summary>Pictures from the video, evenly spaced over its length; null until each one is ready.</summary>
    public void SetThumbnails(BitmapSource?[] thumbnails)
    {
        _thumbnails = thumbnails;
        InvalidateVisual();
    }

    /// <summary>How many pictures have arrived so far.</summary>
    public int ThumbnailCount => _thumbnails.Count(t => t != null);

    public double TimeAt(double x) => TimelineLayout.TimeAt(x, _pixelsPerSecond);

    protected override Size MeasureOverride(Size availableSize) =>
        new(_timeline == null ? 0 : TimelineLayout.Width(_timeline, _pixelsPerSecond), TimelineLayout.Height);

    private void OnTimelineChanged(object? sender, EventArgs e)
    {
        // A new crop or look leaves the pieces as they were (the same list): nothing here to draw again, which matters
        // while a slider sends a change for every pixel it moves.
        if (ReferenceEquals(_timeline?.Segments, _drawnSegments))
        {
            return;
        }
        _drawnSegments = _timeline?.Segments;
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
        TimelineHit hit = TimelineLayout.HitTest(_timeline, _pixelsPerSecond, point);
        _grabX = _pointerX = point.X;
        _gestureIndex = hit.Index;

        switch (hit.Part)
        {
            case TimelinePart.Ruler:
            case TimelinePart.Body:
            case TimelinePart.None:
                _gesture = Gesture.Scrub;
                Scrub(point.X);
                break;
            case TimelinePart.Grip:
                Select(hit.Index);
                _gesture = Gesture.Reorder;
                _reordering = false;
                break;
            case TimelinePart.StartEdge:
            case TimelinePart.EndEdge:
                Select(hit.Index);
                VideoSegment segment = _timeline.Segments[hit.Index];
                _originalStart = segment.Start;
                _originalEnd = segment.End;
                _gesture = hit.Part == TimelinePart.StartEdge ? Gesture.TrimStart : Gesture.TrimEnd;
                _timeline.BeginGesture();
                break;
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
        _pointerX = point.X;
        switch (_gesture)
        {
            case Gesture.None:
                UpdateHover(TimelineLayout.HitTest(_timeline, _pixelsPerSecond, point));
                return;
            case Gesture.Scrub:
                Scrub(point.X);
                break;
            case Gesture.TrimStart:
            {
                double start = _originalStart + (point.X - _grabX) / _pixelsPerSecond * _timeline.Segments[_gestureIndex].Rate;
                _timeline.TrimStart(_gestureIndex, start);
                VideoSegment segment = _timeline.Segments[_gestureIndex];
                _bubble = $"{TimeText.Format(segment.Start)} → {TimeText.Format(segment.End)}  ({segment.Length:0.0} s)";
                Previewed?.Invoke(this, segment.Start);
                break;
            }
            case Gesture.TrimEnd:
            {
                double end = _originalEnd + (point.X - _grabX) / _pixelsPerSecond * _timeline.Segments[_gestureIndex].Rate;
                _timeline.TrimEnd(_gestureIndex, end);
                VideoSegment segment = _timeline.Segments[_gestureIndex];
                _bubble = $"{TimeText.Format(segment.Start)} → {TimeText.Format(segment.End)}  ({segment.Length:0.0} s)";
                Previewed?.Invoke(this, Math.Max(segment.Start, segment.End - 0.05)); // the last picture that stays
                break;
            }
            case Gesture.Reorder:
                if (Math.Abs(point.X - _grabX) > ReorderThreshold)
                {
                    _reordering = true;
                }
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_gesture == Gesture.None || _timeline == null)
        {
            return;
        }

        Gesture finished = _gesture;
        _gesture = Gesture.None;
        _bubble = null;
        ReleaseMouseCapture();

        if (finished is Gesture.TrimStart or Gesture.TrimEnd)
        {
            _timeline.EndGesture();
        }
        else if (finished == Gesture.Reorder && _reordering)
        {
            int place = DropPlace();
            _reordering = false;
            if (place != _gestureIndex)
            {
                _selected = place; // already selected when the drag began; the piece keeps being the selected one
                _timeline.Move(_gestureIndex, place);
                _playhead = _timeline.StartOf(place); // the order changed, so the old moment means something else now
                Scrubbed?.Invoke(this, _playhead);
            }
        }
        _reordering = false;
        InvalidateVisual();
        GestureFinished?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Lost without a release (another window took the mouse, for instance): finish like a release so nothing stays half done.
        if (_gesture != Gesture.None && _timeline != null)
        {
            if (_gesture is Gesture.TrimStart or Gesture.TrimEnd)
            {
                _timeline.EndGesture();
            }
            _gesture = Gesture.None;
            _reordering = false;
            _bubble = null;
            InvalidateVisual();
            GestureFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_timeline == null)
        {
            return;
        }

        // The menu acts on the piece under the pointer.
        TimelineHit hit = TimelineLayout.HitTest(_timeline, _pixelsPerSecond, e.GetPosition(this));
        _menuAllowed = hit.Index >= 0;
        if (_menuAllowed)
        {
            Select(hit.Index);
            Scrub(e.GetPosition(this).X);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_gesture == Gesture.None)
        {
            UpdateHover(new TimelineHit(TimelinePart.None, -1));
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

    private void UpdateHover(TimelineHit hit)
    {
        if (hit == _hover)
        {
            return;
        }

        _hover = hit;
        Cursor = hit.Part switch
        {
            TimelinePart.Grip => Cursors.SizeAll,
            TimelinePart.StartEdge or TimelinePart.EndEdge => Cursors.SizeWE,
            _ => Cursors.Arrow,
        };
    }

    private void Select(int index)
    {
        if (index != _selected)
        {
            _selected = index;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Moves the playhead to the x of the pointer, and the selection to the piece that plays there.
    private void Scrub(double x)
    {
        if (_timeline == null)
        {
            return;
        }

        _playhead = Math.Clamp(TimeAt(x), 0, _timeline.Duration);
        Select(_timeline.Locate(_playhead).Index);
        Scrubbed?.Invoke(this, _playhead);
        InvalidateVisual();
    }

    // Where the dragged block would land, from where its middle is now.
    private int DropPlace()
    {
        (double left, double right) = TimelineLayout.Span(_timeline!, _gestureIndex, _pixelsPerSecond);
        double center = (left + right) / 2 + _pointerX - _grabX;
        return TimelineLayout.DropIndex(_timeline!, _gestureIndex, _pixelsPerSecond, center);
    }

    // ---------- drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // so the empty parts take clicks too
        if (_timeline == null)
        {
            return;
        }

        var palette = new Palette(this);
        DrawRuler(dc, palette);
        for (int i = 0; i < _timeline.Segments.Count; i++)
        {
            bool dragged = _gesture == Gesture.Reorder && _reordering && i == _gestureIndex;
            if (dragged)
            {
                dc.PushOpacity(0.3);
            }
            DrawBlock(dc, palette, i, 0);
            if (dragged)
            {
                dc.Pop();
            }
        }

        if (_gesture == Gesture.Reorder && _reordering)
        {
            DrawReorder(dc, palette);
        }
        DrawPlayhead(dc, palette);
        if (_bubble != null)
        {
            DrawBubble(dc, palette, _bubble);
        }
    }

    private void DrawRuler(DrawingContext dc, Palette palette)
    {
        double step = TimelineLayout.RulerStep(_pixelsPerSecond);
        double duration = _timeline!.Duration;
        var pen = new Pen(palette.BorderStrong, 1);
        var minor = new Pen(palette.Border, 1);

        dc.DrawLine(minor, new Point(TimelineLayout.Padding, TimelineLayout.RulerHeight), new Point(TimelineLayout.XOf(duration, _pixelsPerSecond), TimelineLayout.RulerHeight));
        for (int n = 0; n * step <= duration + 0.0001; n++)
        {
            double time = n * step;
            double x = Math.Round(TimelineLayout.XOf(time, _pixelsPerSecond)) + 0.5;
            dc.DrawLine(pen, new Point(x, TimelineLayout.RulerHeight - 10), new Point(x, TimelineLayout.RulerHeight));
            dc.DrawText(palette.Text(TimeText.Mark(time, step), 11, palette.TextSecondary), new Point(x + 4, 4));
            if (step * _pixelsPerSecond >= 60)
            {
                for (int m = 1; m < 5; m++)
                {
                    double minorTime = time + step * m / 5;
                    if (minorTime > duration)
                    {
                        break;
                    }
                    double mx = Math.Round(TimelineLayout.XOf(minorTime, _pixelsPerSecond)) + 0.5;
                    dc.DrawLine(minor, new Point(mx, TimelineLayout.RulerHeight - 5), new Point(mx, TimelineLayout.RulerHeight));
                }
            }
        }
    }

    // One piece as a block: a title strip on top, pictures from the video under it, and handles on the sides when selected.
    private void DrawBlock(DrawingContext dc, Palette palette, int index, double offsetX)
    {
        VideoSegment segment = _timeline!.Segments[index];
        (double left, double right) = TimelineLayout.Span(_timeline, index, _pixelsPerSecond);
        var rect = new Rect(left + 1 + offsetX, TimelineLayout.TrackTop, Math.Max(3, right - left - 2), TimelineLayout.TrackHeight);
        bool selected = index == _selected;

        var shape = new RectangleGeometry(rect, 6, 6);
        dc.DrawGeometry(palette.Input, null, shape);
        dc.PushClip(shape);

        // The pictures.
        double picturesTop = rect.Top + TimelineLayout.GripHeight;
        double picturesHeight = TimelineLayout.TrackHeight - TimelineLayout.GripHeight;
        BitmapSource? sample = _thumbnails.FirstOrDefault(t => t != null);
        double tile = sample == null ? picturesHeight * 16 / 9 : Math.Max(20, picturesHeight * sample.PixelWidth / sample.PixelHeight);
        for (double x = rect.Left; x < rect.Right; x += tile)
        {
            double sourceTime = segment.Start + (x + tile / 2 - (left + offsetX)) / _pixelsPerSecond * segment.Rate;
            BitmapSource? picture = PictureAt(sourceTime);
            if (picture != null)
            {
                dc.DrawImage(picture, new Rect(x, picturesTop, tile, picturesHeight));
            }
        }
        if (segment.Muted)
        {
            dc.DrawRectangle(palette.Dim, null, new Rect(rect.Left, picturesTop, rect.Width, picturesHeight));
        }

        // The title strip.
        var strip = new Rect(rect.Left, rect.Top, rect.Width, TimelineLayout.GripHeight);
        dc.DrawRectangle(selected ? palette.Accent : palette.Surface, null, strip);
        if (rect.Width > 46)
        {
            string title = $"{index + 1} · {TimeText.Format(segment.PlayLength)}"
                + (segment.IsRetimed ? $" · {TimeText.Speed(segment.Rate)}" : "")
                + (segment.Muted ? " · sin sonido" : "");
            FormattedText text = palette.Text(title, 11, selected ? palette.OnAccent : palette.TextPrimary);
            text.MaxTextWidth = rect.Width - 16;
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(text, new Point(rect.Left + 8, rect.Top + 3));
        }
        dc.Pop();

        dc.DrawGeometry(null, new Pen(selected ? palette.Accent : palette.BorderStrong, selected ? 2 : 1), new RectangleGeometry(rect, 6, 6));
        if (selected)
        {
            DrawHandle(dc, palette, rect.Left + 1);
            DrawHandle(dc, palette, rect.Right - 1);
        }
    }

    private static void DrawHandle(DrawingContext dc, Palette palette, double x)
    {
        double top = TimelineLayout.TrackTop + TimelineLayout.GripHeight + 10;
        var pill = new Rect(x - 3, top, 6, TimelineLayout.TrackHeight - TimelineLayout.GripHeight - 20);
        dc.DrawRoundedRectangle(palette.OnAccent, new Pen(palette.Accent, 1.5), pill, 3, 3);
    }

    // The picture of the video nearest to a moment of the original that is already loaded.
    private BitmapSource? PictureAt(double sourceTime)
    {
        int count = _thumbnails.Length;
        if (count == 0 || _timeline!.SourceDuration <= 0)
        {
            return null;
        }

        int wanted = Math.Clamp((int)(sourceTime / _timeline.SourceDuration * count), 0, count - 1);
        for (int distance = 0; distance < count; distance++)
        {
            if (wanted - distance >= 0 && _thumbnails[wanted - distance] is { } before)
            {
                return before;
            }
            if (wanted + distance < count && _thumbnails[wanted + distance] is { } after)
            {
                return after;
            }
        }
        return null;
    }

    // The block being dragged follows the pointer, and a line shows the gap where it would land.
    private void DrawReorder(DrawingContext dc, Palette palette)
    {
        dc.PushOpacity(0.85);
        DrawBlock(dc, palette, _gestureIndex, _pointerX - _grabX);
        dc.Pop();

        int place = DropPlace();
        double x = TimelineLayout.DropLineX(_timeline!, _gestureIndex, _pixelsPerSecond, place);
        dc.DrawRoundedRectangle(palette.Accent, null, new Rect(x - 1.5, TimelineLayout.TrackTop - 4, 3, TimelineLayout.TrackHeight + 8), 1.5, 1.5);
    }

    private void DrawPlayhead(DrawingContext dc, Palette palette)
    {
        double x = Math.Round(PlayheadX) + 0.5;
        dc.DrawLine(new Pen(palette.Accent, 2), new Point(x, 8), new Point(x, TimelineLayout.Height - 4));
        var head = new StreamGeometry();
        using (StreamGeometryContext context = head.Open())
        {
            context.BeginFigure(new Point(x - 6, 0), true, true);
            context.LineTo(new Point(x + 6, 0), true, false);
            context.LineTo(new Point(x, 10), true, false);
        }
        head.Freeze();
        dc.DrawGeometry(palette.Accent, null, head);
    }

    private void DrawBubble(DrawingContext dc, Palette palette, string message)
    {
        FormattedText text = palette.Text(message, 12, palette.TextPrimary);
        double width = text.Width + 16;
        double x = Math.Clamp(_pointerX - width / 2, 2, Math.Max(2, ActualWidth - width - 2));
        var box = new Rect(x, TimelineLayout.TrackTop - 30, width, 22);
        dc.DrawRoundedRectangle(palette.Surface, new Pen(palette.BorderStrong, 1), box, 6, 6);
        dc.DrawText(text, new Point(box.Left + 8, box.Top + 3));
    }

    // The colours of the current theme, looked up each time it is drawn so a theme change shows at once.
    private sealed class Palette
    {
        private readonly FrameworkElement _owner;
        private readonly double _pixelsPerDip;

        public Palette(FrameworkElement owner)
        {
            _owner = owner;
            _pixelsPerDip = VisualTreeHelper.GetDpi(owner).PixelsPerDip;
            Accent = Find("Accent", Brushes.IndianRed);
            OnAccent = Find("OnAccent", Brushes.White);
            Surface = Find("Surface", Brushes.DimGray);
            Input = Find("InputBackground", Brushes.Black);
            Border = Find("Border", Brushes.Gray);
            BorderStrong = Find("BorderStrong", Brushes.Gray);
            TextPrimary = Find("TextPrimary", Brushes.White);
            TextSecondary = Find("TextSecondary", Brushes.Silver);
        }

        public Brush Accent { get; }

        public Brush OnAccent { get; }

        public Brush Surface { get; }

        public Brush Input { get; }

        public Brush Border { get; }

        public Brush BorderStrong { get; }

        public Brush TextPrimary { get; }

        public Brush TextSecondary { get; }

        public Brush Dim { get; } = Freeze(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));

        public FormattedText Text(string text, double size, Brush brush) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, _pixelsPerDip);

        private Brush Find(string key, Brush fallback) => _owner.TryFindResource(key) as Brush ?? fallback;

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
