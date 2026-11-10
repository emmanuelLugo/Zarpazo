using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Overlay;

namespace Zarpazo.Editor;

/// <summary>Holds the drawn annotations with their undo/redo history and paints them over the screenshot.</summary>
internal sealed class AnnotationCanvas : FrameworkElement
{
    private const double GripSize = 7;

    private static readonly Pen SelectionOutline = CreateSelectionOutline();

    private readonly record struct Edit(Action Undo, Action Redo);

    private readonly List<Annotation> _done = [];
    private readonly Stack<Edit> _history = new();
    private readonly Stack<Edit> _future = new();
    private Annotation? _preview;
    private Annotation? _selected;
    private Rect _clipArea = Rect.Empty;

    /// <summary>Strokes are cut off at the capture area, like they will be in the saved image.</summary>
    public Rect ClipArea
    {
        set
        {
            _clipArea = value;
            InvalidateVisual();
        }
    }

    /// <summary>The annotation picked with the move tool, drawn with a dashed outline that is not part of the image.</summary>
    public Annotation? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<Annotation> Items => _done;

    /// <summary>Puts annotations restored from a saved capture on the canvas. They are not part of the undo history.</summary>
    public void Load(IEnumerable<Annotation> annotations)
    {
        _done.AddRange(annotations);
        InvalidateVisual();
    }

    /// <summary>The number the next step badge gets: one above the highest on screen, so numbers never repeat.</summary>
    public int NextStepNumber => _done.OfType<NumberAnnotation>().Select(badge => badge.Number).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The annotation being dragged right now; call <see cref="Commit"/> when it is finished.</summary>
    public void Begin(Annotation annotation) => _preview = annotation;

    public void Commit(Annotation annotation)
    {
        _preview = null;
        if (!annotation.IsEmpty)
        {
            int index = _done.Count;
            _done.Add(annotation);
            Record(new Edit(() => _done.Remove(annotation), () => _done.Insert(index, annotation)));
        }
        InvalidateVisual();
    }

    /// <summary>The topmost annotation under the point, if any.</summary>
    public Annotation? HitTest(Point point, double tolerance) => _done.LastOrDefault(a => a.HitTest(point, tolerance));

    /// <summary>Notes that an annotation was already moved, so that undo and redo can move it back and forth.</summary>
    public void RecordMove(Annotation annotation, Vector total)
    {
        if (total != default)
        {
            Record(new Edit(() => annotation.Offset(-total), () => annotation.Offset(total)));
        }
    }

    /// <summary>Notes that an annotation was already stretched, so that undo and redo can stretch it back and forth.</summary>
    public void RecordResize(Annotation annotation, Rect before, Rect after)
    {
        if (before != after)
        {
            Record(new Edit(() => annotation.Resize(after, before), () => annotation.Resize(before, after)));
        }
    }

    /// <summary>The grip of the selected annotation under the point, if it can be stretched.</summary>
    public SelectionHandle HandleAt(Point point, double tolerance)
    {
        if (_selected is not { CanResize: true })
        {
            return SelectionHandle.None;
        }

        foreach ((SelectionHandle handle, Point at) in GripPoints(Outline(_selected)))
        {
            if ((point - at).Length <= tolerance)
            {
                return handle;
            }
        }
        return SelectionHandle.None;
    }

    /// <summary>The eight grips of an outline: the corners and the middle of each side.</summary>
    internal static IReadOnlyList<(SelectionHandle Handle, Point At)> GripPoints(Rect outline)
    {
        double midX = outline.Left + outline.Width / 2;
        double midY = outline.Top + outline.Height / 2;
        return
        [
            (SelectionHandle.NorthWest, outline.TopLeft),
            (SelectionHandle.North, new Point(midX, outline.Top)),
            (SelectionHandle.NorthEast, outline.TopRight),
            (SelectionHandle.East, new Point(outline.Right, midY)),
            (SelectionHandle.SouthEast, outline.BottomRight),
            (SelectionHandle.South, new Point(midX, outline.Bottom)),
            (SelectionHandle.SouthWest, outline.BottomLeft),
            (SelectionHandle.West, new Point(outline.Left, midY)),
        ];
    }

    private static Rect Outline(Annotation annotation)
    {
        Rect outline = annotation.Bounds;
        outline.Inflate(3, 3);
        return outline;
    }

    public void DeleteSelected()
    {
        if (_selected is not { } annotation)
        {
            return;
        }

        int index = _done.IndexOf(annotation);
        _done.RemoveAt(index);
        _selected = null;
        Record(new Edit(() => _done.Insert(index, annotation), () => _done.Remove(annotation)));
        InvalidateVisual();
    }

    public void Undo()
    {
        if (_history.Count == 0)
        {
            return;
        }
        Edit edit = _history.Pop();
        edit.Undo();
        _future.Push(edit);
        AfterHistoryChange();
    }

    public void Redo()
    {
        if (_future.Count == 0)
        {
            return;
        }
        Edit edit = _future.Pop();
        edit.Redo();
        _history.Push(edit);
        AfterHistoryChange();
    }

    public void Clear()
    {
        _done.Clear();
        _history.Clear();
        _future.Clear();
        _preview = null;
        _selected = null;
        _clipArea = Rect.Empty;
        InvalidateVisual();
    }

    /// <summary>The cropped screenshot with the annotations on top.</summary>
    /// <param name="crop">The capture area, already cut out of the screenshot.</param>
    /// <param name="x">Pixel column of the crop inside the whole screenshot.</param>
    /// <param name="y">Pixel row of the crop inside the whole screenshot.</param>
    /// <param name="scaleX">Image pixels per layout unit, horizontally.</param>
    /// <param name="scaleY">Image pixels per layout unit, vertically.</param>
    public BitmapSource Compose(BitmapSource crop, int x, int y, double scaleX, double scaleY)
    {
        if (_done.Count == 0)
        {
            return crop;
        }

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor); // keeps pixelated blocks sharp
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(crop, new Rect(0, 0, crop.PixelWidth, crop.PixelHeight));
            context.PushTransform(new MatrixTransform(scaleX, 0, 0, scaleY, -x, -y));
            foreach (Annotation annotation in _done)
            {
                annotation.Draw(context);
            }
            context.Pop();
        }

        var result = new RenderTargetBitmap(crop.PixelWidth, crop.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    protected override void OnRender(DrawingContext context)
    {
        if (_clipArea.IsEmpty)
        {
            return;
        }

        context.PushClip(new RectangleGeometry(_clipArea));
        foreach (Annotation annotation in _done)
        {
            annotation.Draw(context);
        }
        _preview?.Draw(context);
        if (_selected != null)
        {
            context.DrawRectangle(null, SelectionOutline, Outline(_selected));
        }
        context.Pop();

        // Outside the clip, so the grips of an annotation at the edge of the area are whole.
        if (_selected is { CanResize: true })
        {
            foreach ((_, Point at) in GripPoints(Outline(_selected)))
            {
                context.DrawRectangle(Brushes.White, SelectionOutline, new Rect(at.X - GripSize / 2, at.Y - GripSize / 2, GripSize, GripSize));
            }
        }
    }

    private void Record(Edit edit)
    {
        _history.Push(edit);
        _future.Clear(); // a new edit makes the undone ones unreachable
    }

    private void AfterHistoryChange()
    {
        if (_selected != null && !_done.Contains(_selected))
        {
            _selected = null; // undo can remove what was selected
        }
        InvalidateVisual();
    }

    private static Pen CreateSelectionOutline()
    {
        var pen = new Pen(Brushes.DeepSkyBlue, 1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }
}
