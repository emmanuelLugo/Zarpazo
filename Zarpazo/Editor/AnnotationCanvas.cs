using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Editor;

/// <summary>Holds the drawn annotations with their undo/redo history and paints them over the screenshot.</summary>
internal sealed class AnnotationCanvas : FrameworkElement
{
    private readonly List<Annotation> _done = [];
    private readonly Stack<Annotation> _undone = new();
    private Annotation? _preview;
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

    /// <summary>The number the next step badge gets. Undoing a badge frees its number again.</summary>
    public int NextStepNumber => _done.OfType<NumberAnnotation>().Count() + 1;

    /// <summary>The annotation being dragged right now; call <see cref="Commit"/> when it is finished.</summary>
    public void Begin(Annotation annotation) => _preview = annotation;

    public void Commit(Annotation annotation)
    {
        _preview = null;
        if (!annotation.IsEmpty)
        {
            _done.Add(annotation);
            _undone.Clear();
        }
        InvalidateVisual();
    }

    public void Undo()
    {
        if (_done.Count == 0)
        {
            return;
        }
        _undone.Push(_done[^1]);
        _done.RemoveAt(_done.Count - 1);
        InvalidateVisual();
    }

    public void Redo()
    {
        if (_undone.Count == 0)
        {
            return;
        }
        _done.Add(_undone.Pop());
        InvalidateVisual();
    }

    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        _preview = null;
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
        context.Pop();
    }
}
