using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using ShapePath = System.Windows.Shapes.Path;

namespace Zarpazo.Recording;

// The blurs, texts and arrows over the video: their preview on the picture, the tools that make them and the drag that moves
// them, and their panel. What is drawn in the saved video comes from OverlayLayer; both place everything the same way.
public partial class VideoEditWindow
{
    private const double OverlayDefaultSeconds = 3;
    private const double DragStartPixels = 4;

    private static readonly Color[] OverlayPalette =
    [
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x14, 0x14, 0x14), Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0xF0, 0x8C, 0x2E),
        Color.FromRgb(0xF5, 0xC5, 0x42), Color.FromRgb(0x3F, 0xB9, 0x50), Color.FromRgb(0x4C, 0x8D, 0xFF),
    ];

    private enum OverlayGrab
    {
        None,
        Create, // dragging out a new blur or arrow
        Move,
        Handle, // a corner of a blur, an end of an arrow
    }

    private readonly List<FrameworkElement> _overlayShapes = [];
    private IReadOnlyList<VideoOverlay>? _seenOverlays; // what the window last saw of them
    private OverlayKind? _overlayTool;
    private bool _showingOverlay; // the panel is being filled from the overlay chosen, which is not a change
    private bool _typingOverlay;  // the words are being typed: all of it is one step to undo
    private OverlayGrab _grab;
    private int _grabOverlay;
    private int _grabHandle;
    private (double X, double Y) _grabFrom;
    private Point _grabPoint;
    private VideoOverlay _grabStart;

    // The overlays' panel is open and nothing is playing: the picture can be worked on.
    private bool OverlayEditing => OverlayTab.IsChecked == true && !_playing && _timeline != null;

    private void BuildOverlayInks()
    {
        foreach (Color ink in OverlayPalette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "overlayInk",
                Background = new SolidColorBrush(ink),
                Tag = ink,
                Width = 30,
                Height = 30,
            };
            swatch.Click += OnOverlayInk;
            OverlayInks.Children.Add(swatch);
        }
    }

    // ---------- placing

    // A point of the stage (pixels of the picture as it is shown) as shares of the whole recording.
    private (double X, double Y) StageToRecording(Point point)
    {
        double x = _shownView.X + point.X / Stage.Width * _shownView.Width;
        double y = _shownView.Y + point.Y / Stage.Height * _shownView.Height;
        if (_layout is { } layout)
        {
            x = (x * layout.Width - layout.Content.X) / layout.Content.Width;
            y = (y * layout.Height - layout.Content.Y) / layout.Content.Height;
        }
        return _area is { } area ? area.FromCrop(x, y) : (x, y);
    }

    // A point of the recording (shares of it) as a point of the stage.
    private Point RecordingToStage(double x, double y)
    {
        (double cx, double cy) = _area is { } area ? area.ToCrop(x, y) : (x, y);
        (double px, double py) = CursorLayer.ToPicture(cx, cy, _shownView, _layout, Stage.Width, Stage.Height);
        return new Point(px, py);
    }

    // How much bigger than in the recording the picture shows things now: the zoom, and the frame that shrinks the recording.
    private double ShownScale => _shownView.Scale * (_layout is { } frame ? frame.Content.Width / (double)frame.Width : 1);

    // ---------- the preview

    // Makes the elements that show the overlays, in the recording's pixels (the player's own, shifted by the crop): the zoom and
    // the frame then carry them with the picture. Done when the overlays, the crop or the frame change; time only shows or hides them.
    private void RebuildOverlayShapes()
    {
        OverlayCanvas.Children.Clear();
        _overlayShapes.Clear();
        if (_timeline == null || _sourceWidth <= 0 || double.IsNaN(Stage.Height))
        {
            return;
        }

        foreach (VideoOverlay overlay in _timeline.Overlays)
        {
            FrameworkElement shape = overlay.Kind switch
            {
                OverlayKind.Blur => BlurShape(overlay),
                OverlayKind.Arrow => ArrowPreview(overlay),
                _ => TextShape(overlay),
            };
            shape.Visibility = Visibility.Collapsed;
            _overlayShapes.Add(shape);
            OverlayCanvas.Children.Add(shape);
        }
        UpdateOverlayShapes(_playhead);
    }

    // A point of the recording in the pixels of the stage before the camera moves it: the recording's, less the crop.
    private Point InPicture(double x, double y) => new(x * _sourceWidth - (_area?.Pixels.X ?? 0), y * _sourceHeight - (_area?.Pixels.Y ?? 0));

    private FrameworkElement BlurShape(VideoOverlay overlay)
    {
        Point from = InPicture(overlay.X, overlay.Y);
        Point to = InPicture(overlay.X2, overlay.Y2);
        double radius = Stage.Height * 0.065; // about as strong as OverlayLayer's, which this preview stands in for
        double pad = radius; // the blur reads past the rectangle's sides, so they do not fade to nothing
        var rectangle = new Rectangle
        {
            Width = to.X - from.X + pad * 2,
            Height = to.Y - from.Y + pad * 2,
            Fill = new VisualBrush(Player)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(overlay.X * _sourceWidth - pad, overlay.Y * _sourceHeight - pad, to.X - from.X + pad * 2, to.Y - from.Y + pad * 2),
                Stretch = Stretch.Fill,
            },
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        Canvas.SetLeft(rectangle, -pad);
        Canvas.SetTop(rectangle, -pad);
        var host = new Canvas { Width = Math.Max(0, to.X - from.X), Height = Math.Max(0, to.Y - from.Y), ClipToBounds = true };
        host.Children.Add(rectangle);
        Canvas.SetLeft(host, from.X);
        Canvas.SetTop(host, from.Y);
        return host;
    }

    private FrameworkElement TextShape(VideoOverlay overlay)
    {
        double size = Math.Max(6, overlay.Extent * Stage.Height);
        var text = new TextBlock
        {
            Text = overlay.Text,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = size,
            Foreground = new SolidColorBrush(overlay.Paint),
            Effect = new DropShadowEffect { ShadowDepth = Math.Max(1, size * 0.05) * 1.41, Direction = 315, BlurRadius = 0, Opacity = 0.59, Color = Colors.Black },
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point at = InPicture(overlay.X, overlay.Y);
        Canvas.SetLeft(text, at.X);
        Canvas.SetTop(text, at.Y);
        return text;
    }

    private FrameworkElement ArrowPreview(VideoOverlay overlay)
    {
        var container = new Canvas();
        Point tail = InPicture(overlay.X, overlay.Y);
        Point tip = InPicture(overlay.X2, overlay.Y2);
        double thickness = Math.Max(1.5, overlay.Extent * Stage.Height);
        if (ArrowShape.For((tail.X, tail.Y), (tip.X, tip.Y), thickness) is not { } shape)
        {
            return container;
        }

        var brush = new SolidColorBrush(overlay.Paint);
        container.Effect = new DropShadowEffect { ShadowDepth = thickness * 0.35, Direction = 270, BlurRadius = 0, Opacity = 0.43, Color = Colors.Black };
        container.Children.Add(new ShapePath
        {
            Data = new LineGeometry(tail, new Point(shape.Neck.X, shape.Neck.Y)),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Flat,
        });
        container.Children.Add(new System.Windows.Shapes.Polygon
        {
            Points = [new Point(shape.Tip.X, shape.Tip.Y), new Point(shape.Left.X, shape.Left.Y), new Point(shape.Right.X, shape.Right.Y)],
            Fill = brush,
        });
        return container;
    }

    // Shows the overlays that are on at this moment of the edited video, and fades the ones that are coming or going.
    private void UpdateOverlayShapes(double time)
    {
        if (_timeline == null)
        {
            return;
        }

        double original = CursorLayer.ToOriginal(_timeline.Segments, time);
        for (int i = 0; i < _overlayShapes.Count && i < _timeline.Overlays.Count; i++)
        {
            // The one being worked on shows whole for as long as it lasts, even at its first moment, where it is still fading in.
            double opacity = i == OverlayLane.SelectedOverlay && OverlayEditing && IsWithin(_timeline.Overlays[i], original)
                ? 1
                : _timeline.Overlays[i].OpacityAt(original);
            _overlayShapes[i].Visibility = opacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            _overlayShapes[i].Opacity = opacity;
        }
    }

    // The preview's part of ApplyZoomPreview: what is on now, and the outline and handles of the one chosen.
    private void UpdateOverlayLayer(double time, ZoomView view)
    {
        UpdateOverlayShapes(time);
        UpdateOverlayHandles(time, view);
    }

    private bool IsOverlayOn(int index)
    {
        if (_timeline == null || index < 0 || index >= _timeline.Overlays.Count)
        {
            return false;
        }
        return IsWithin(_timeline.Overlays[index], CursorLayer.ToOriginal(_timeline.Segments, _playhead));
    }

    // Whether a moment of the original falls in the stretch the overlay lasts (a text is still invisible at its very first moment).
    private static bool IsWithin(VideoOverlay overlay, double original) => original >= overlay.Start && original < overlay.End;

    // The points of the chosen overlay a hand can take, on the stage: the corners of a blur, the two ends of an arrow.
    private IReadOnlyList<Point> OverlayHandlePoints(VideoOverlay overlay) => overlay.Kind switch
    {
        OverlayKind.Blur =>
        [
            RecordingToStage(overlay.X, overlay.Y), RecordingToStage(overlay.X2, overlay.Y),
            RecordingToStage(overlay.X, overlay.Y2), RecordingToStage(overlay.X2, overlay.Y2),
        ],
        OverlayKind.Arrow => [RecordingToStage(overlay.X, overlay.Y), RecordingToStage(overlay.X2, overlay.Y2)],
        _ => [],
    };

    // Where an overlay is on the stage, as a box.
    private Rect OverlayBox(int index)
    {
        VideoOverlay overlay = _timeline!.Overlays[index];
        Point from = RecordingToStage(overlay.X, overlay.Y);
        if (overlay.Kind == OverlayKind.Text)
        {
            Size size = index < _overlayShapes.Count ? _overlayShapes[index].DesiredSize : default;
            return new Rect(from, new Size(size.Width * ShownScale, size.Height * ShownScale));
        }
        return new Rect(from, RecordingToStage(overlay.X2, overlay.Y2));
    }

    private void UpdateOverlayHandles(double time, ZoomView view)
    {
        OverlayHandles.Children.Clear();
        int index = OverlayLane.SelectedOverlay;
        bool show = OverlayEditing && IsOverlayOn(index) && !double.IsNaN(Stage.Width);
        OverlayHandles.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        VideoOverlay overlay = _timeline!.Overlays[index];
        double line = Math.Max(2, Stage.Width / 320); // the picture is shrunk to fit, so the lines are drawn thicker in its pixels
        var accent = new SolidColorBrush(Color.FromRgb(0x8E, 0x6C, 0xEF));
        if (overlay.Kind != OverlayKind.Arrow)
        {
            Rect box = OverlayBox(index);
            var outline = new Rectangle
            {
                Width = box.Width,
                Height = box.Height,
                Stroke = accent,
                StrokeThickness = line,
                StrokeDashArray = [4, 3],
            };
            Canvas.SetLeft(outline, box.X);
            Canvas.SetTop(outline, box.Y);
            OverlayHandles.Children.Add(outline);
        }
        foreach (Point point in OverlayHandlePoints(overlay))
        {
            double size = line * 5;
            var handle = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Stroke = accent, StrokeThickness = line / 2 };
            Canvas.SetLeft(handle, point.X - size / 2);
            Canvas.SetTop(handle, point.Y - size / 2);
            OverlayHandles.Children.Add(handle);
        }
    }

    // ---------- the panel

    private void OnOverlayTool(object sender, RoutedEventArgs e)
    {
        var tile = (RadioButton)sender;
        OverlayKind kind = Enum.Parse<OverlayKind>((string)tile.Tag);
        if (_overlayTool == kind)
        {
            tile.IsChecked = false; // a second click puts the tool down
            _overlayTool = null;
        }
        else
        {
            _overlayTool = kind;
            OverlayLane.SelectedOverlay = -1;
        }
        Stage.Cursor = _overlayTool != null ? Cursors.Cross : null;
        OverlayHint.Text = _overlayTool switch
        {
            OverlayKind.Blur => "Arrastra sobre el video para dibujar la zona que se desenfoca, o haz un clic para poner una.",
            OverlayKind.Text => "Haz clic donde va el texto y escríbelo en el cuadro de abajo.",
            OverlayKind.Arrow => "Arrastra desde donde sale la flecha hasta lo que señala, o haz un clic para poner una que apunte ahí.",
            _ => OverlayHintText,
        };
        ApplyZoomPreview();
    }

    private const string OverlayHintText = "Elige una herramienta y haz clic o arrastra sobre el video, en el momento donde está la línea roja. Cada capa dura 3 segundos: estira o mueve su bloque en la fila de abajo de la línea de tiempo.";

    private void PutToolDown()
    {
        _overlayTool = null;
        foreach (RadioButton tile in new[] { BlurToolTile, TextToolTile, ArrowToolTile })
        {
            tile.IsChecked = false;
        }
        OverlayHint.Text = OverlayHintText;
        Stage.Cursor = null;
    }

    private void OnOverlaySelectionChanged(object? sender, EventArgs e)
    {
        if (OverlayLane.SelectedOverlay >= 0)
        {
            StopPlayback(); // choosing one shows its picture to work on
            if (Lane.SelectedZoom >= 0)
            {
                Lane.SelectedZoom = -1;
            }
            PutToolDown();
            OverlayTab.IsChecked = true;
        }
        ShowOverlayDetails();
        ApplyZoomPreview();
        RefreshState();
    }

    // The words, colour and size of the overlay chosen; shown without that counting as a change.
    private void ShowOverlayDetails()
    {
        int index = OverlayLane.SelectedOverlay;
        bool chosen = _timeline != null && index >= 0 && index < _timeline.Overlays.Count;
        OverlayDetails.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        if (!chosen)
        {
            return;
        }

        VideoOverlay overlay = _timeline!.Overlays[index];
        _showingOverlay = true;
        try
        {
            OverlayDetailsTitle.Text = overlay.Kind switch { OverlayKind.Blur => "Desenfoque elegido", OverlayKind.Arrow => "Flecha elegida", _ => "Texto elegido" };
            OverlayTextBlock.Visibility = overlay.Kind == OverlayKind.Text ? Visibility.Visible : Visibility.Collapsed;
            OverlayInkBlock.Visibility = overlay.Kind == OverlayKind.Blur ? Visibility.Collapsed : Visibility.Visible;
            OverlayBlurNote.Visibility = overlay.Kind == OverlayKind.Blur ? Visibility.Visible : Visibility.Collapsed;
            if (overlay.Kind == OverlayKind.Text && OverlayTextBox.Text != overlay.Text)
            {
                OverlayTextBox.Text = overlay.Text;
            }
            if (overlay.Kind != OverlayKind.Blur)
            {
                bool arrow = overlay.Kind == OverlayKind.Arrow;
                OverlaySizeSlider.Minimum = arrow ? 0.003 : 0.02;
                OverlaySizeSlider.Maximum = arrow ? 0.03 : 0.15;
                OverlaySizeSlider.Value = overlay.Extent;
                OverlaySizeLabel.Text = arrow ? "Grosor" : "Tamaño";
                OverlaySizeText.Text = $"{(overlay.Extent * 100).ToString("0.#", CultureInfo.CurrentCulture)} %";
                foreach (RadioButton swatch in OverlayInks.Children.OfType<RadioButton>())
                {
                    swatch.IsChecked = (Color)swatch.Tag == overlay.Paint;
                }
            }
        }
        finally
        {
            _showingOverlay = false;
        }
    }

    private void OnOverlayTextChanged(object sender, TextChangedEventArgs e)
    {
        int index = OverlayLane.SelectedOverlay;
        if (_showingOverlay || !CanEdit || index < 0 || index >= _timeline!.Overlays.Count || _timeline.Overlays[index].Kind != OverlayKind.Text)
        {
            return;
        }
        if (!_typingOverlay)
        {
            _typingOverlay = true;
            _timeline.BeginGesture(); // everything typed until the box is left is one step to undo
        }
        _timeline.SetOverlay(index, _timeline.Overlays[index] with { Text = OverlayTextBox.Text });
    }

    private void OnOverlayTextLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_typingOverlay)
        {
            _typingOverlay = false;
            _timeline?.EndGesture();
        }
    }

    private void OnOverlayInk(object sender, RoutedEventArgs e)
    {
        int index = OverlayLane.SelectedOverlay;
        if (_showingOverlay || !CanEdit || index < 0 || index >= _timeline!.Overlays.Count)
        {
            return;
        }
        _timeline.SetOverlay(index, _timeline.Overlays[index] with { Ink = (Color)((RadioButton)sender).Tag });
    }

    private void OnOverlaySizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _showingOverlay || !CanEdit)
        {
            return; // sliders send changes while the XAML loads, before the rest of the window exists
        }
        int index = OverlayLane.SelectedOverlay;
        if (index < 0 || index >= _timeline!.Overlays.Count)
        {
            return;
        }
        _timeline.SetOverlay(index, _timeline.Overlays[index] with { Size = e.NewValue });
    }

    private void OnDeleteOverlay(object sender, RoutedEventArgs e) => DeleteOverlay();

    private void DeleteOverlay()
    {
        int index = OverlayLane.SelectedOverlay;
        if (CanEdit && index >= 0)
        {
            OverlayLane.SelectedOverlay = -1;
            _timeline!.DeleteOverlay(index);
        }
    }

    // ---------- on the picture

    // Where a new overlay starts: the moment of the original at the playhead, for a few seconds.
    private (double Start, double End) NewOverlayTime()
    {
        double at = _timeline!.Locate(_playhead).SourceTime;
        double end = Math.Min(at + OverlayDefaultSeconds, _timeline.SourceDuration);
        return (Math.Max(0, Math.Min(at, end - VideoOverlay.MinSeconds)), end);
    }

    private int AddOverlayAt(OverlayKind kind, (double X, double Y) from, (double X, double Y) to, string text = "")
    {
        (double start, double end) = NewOverlayTime();
        var overlay = new VideoOverlay(kind, start, end, from.X, from.Y, to.X, to.Y, text);
        return _timeline!.AddOverlay(overlay);
    }

    private void SelectNewOverlay(int index)
    {
        PutToolDown();
        if (index < 0)
        {
            KeptText.Text = "No cabe una capa en este momento del video.";
            return;
        }
        OverlayLane.SelectedOverlay = index;
    }

    private void OverlayStageDown(MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(Stage);
        (double x, double y) = StageToRecording(point);
        e.Handled = true;

        if (_overlayTool is { } tool)
        {
            switch (tool)
            {
                case OverlayKind.Text:
                {
                    int index = AddOverlayAt(OverlayKind.Text, (x, y), (x, y), "Texto");
                    SelectNewOverlay(index);
                    if (index >= 0)
                    {
                        OverlayTextBox.Focus();
                        OverlayTextBox.SelectAll();
                    }
                    break;
                }
                default:
                    _grab = OverlayGrab.Create;
                    _grabOverlay = -1;
                    _grabFrom = (x, y);
                    _grabPoint = point;
                    Stage.CaptureMouse();
                    break;
            }
            return;
        }

        // Without a tool: a handle of the chosen overlay, else the overlay under the pointer.
        int chosen = OverlayLane.SelectedOverlay;
        if (IsOverlayOn(chosen) && HitHandle(_timeline!.Overlays[chosen], point) is var handle and >= 0)
        {
            BeginGrab(OverlayGrab.Handle, chosen, x, y, handle);
            return;
        }
        int hit = HitOverlay(point);
        if (hit < 0)
        {
            OverlayLane.SelectedOverlay = -1;
            return;
        }
        OverlayLane.SelectedOverlay = hit;
        BeginGrab(OverlayGrab.Move, hit, x, y, -1);
    }

    private void BeginGrab(OverlayGrab grab, int index, double x, double y, int handle)
    {
        _grab = grab;
        _grabOverlay = index;
        _grabHandle = handle;
        _grabFrom = (x, y);
        _grabStart = _timeline!.Overlays[index];
        _timeline.BeginGesture(); // the whole drag is one step to undo
        Stage.CaptureMouse();
    }

    private void OverlayStageMove(MouseEventArgs e)
    {
        Point point = e.GetPosition(Stage);
        (double x, double y) = StageToRecording(point);
        switch (_grab)
        {
            case OverlayGrab.Create:
                if (_grabOverlay < 0 && (point - _grabPoint).Length >= DragStartPixels)
                {
                    _timeline!.BeginGesture();
                    _grabOverlay = AddOverlayAt(_overlayTool!.Value, _grabFrom, (x, y));
                    if (_grabOverlay < 0)
                    {
                        _timeline.EndGesture();
                        _grab = OverlayGrab.None;
                        Stage.ReleaseMouseCapture();
                        SelectNewOverlay(-1);
                    }
                }
                else if (_grabOverlay >= 0)
                {
                    _timeline!.SetOverlay(_grabOverlay, _timeline.Overlays[_grabOverlay] with { X2 = x, Y2 = y });
                }
                break;
            case OverlayGrab.Move:
                _timeline!.SetOverlay(_grabOverlay, _grabStart.Moved(x - _grabFrom.X, y - _grabFrom.Y));
                break;
            case OverlayGrab.Handle:
                _timeline!.SetOverlay(_grabOverlay, DragHandle(_grabStart, _grabHandle, x, y));
                break;
        }
    }

    private void OverlayStageUp()
    {
        OverlayGrab grab = _grab;
        _grab = OverlayGrab.None;
        Stage.ReleaseMouseCapture();
        if (grab != OverlayGrab.Create)
        {
            _timeline?.EndGesture();
            return;
        }

        if (_grabOverlay >= 0)
        {
            _timeline!.EndGesture();
            SelectNewOverlay(_grabOverlay);
            return;
        }

        // A click without a drag: one of the usual size, around the click for a blur, ending at it for an arrow.
        (double x, double y) = _grabFrom;
        OverlayKind kind = _overlayTool!.Value;
        int index = kind == OverlayKind.Blur
            ? AddOverlayAt(kind, (Math.Max(0, x - 0.15), Math.Max(0, y - 0.06)), (Math.Min(1, x + 0.15), Math.Min(1, y + 0.06)))
            : AddOverlayAt(kind, (Math.Max(0, x - 0.1), Math.Max(0, y - 0.08)), (x, y));
        SelectNewOverlay(index);
    }

    // The overlay moved so that the handle taken is at (x, y).
    internal static VideoOverlay DragHandle(VideoOverlay start, int handle, double x, double y) => start.Kind == OverlayKind.Arrow
        ? handle == 0 ? start with { X = x, Y = y } : start with { X2 = x, Y2 = y }
        : handle switch
        {
            0 => start with { X = x, Y = y },
            1 => start with { X2 = x, Y = y },
            2 => start with { X = x, Y2 = y },
            _ => start with { X2 = x, Y2 = y },
        };

    // The handle of an overlay under a point of the stage, or -1.
    private int HitHandle(VideoOverlay overlay, Point point)
    {
        double reach = Math.Max(2, Stage.Width / 320) * 4;
        IReadOnlyList<Point> handles = OverlayHandlePoints(overlay);
        for (int i = 0; i < handles.Count; i++)
        {
            if ((handles[i] - point).Length <= reach)
            {
                return i;
            }
        }
        return -1;
    }

    // The overlay under a point of the stage that is on now, the one drawn last first; -1 for none.
    private int HitOverlay(Point point)
    {
        double slack = Math.Max(2, Stage.Width / 320) * 2;
        for (int i = _timeline!.Overlays.Count - 1; i >= 0; i--)
        {
            if (!IsOverlayOn(i))
            {
                continue;
            }

            VideoOverlay overlay = _timeline.Overlays[i];
            bool over;
            if (overlay.Kind == OverlayKind.Arrow)
            {
                Point tail = RecordingToStage(overlay.X, overlay.Y), tip = RecordingToStage(overlay.X2, overlay.Y2);
                double reach = Math.Max(slack * 2, overlay.Extent * Stage.Height * ShownScale / 2 + slack);
                over = DistanceToSegment(point, tail, tip) <= reach;
            }
            else
            {
                Rect box = OverlayBox(i);
                box.Inflate(slack, slack);
                over = box.Contains(point);
            }
            if (over)
            {
                return i;
            }
        }
        return -1;
    }

    private static double DistanceToSegment(Point point, Point from, Point to)
    {
        Vector segment = to - from;
        double lengthSquared = segment.LengthSquared;
        double along = lengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(point - from, segment) / lengthSquared, 0, 1);
        return (point - (from + segment * along)).Length;
    }

    // The pointer over the picture shows what a press would do.
    private Cursor? OverlayCursor(Point point)
    {
        if (_overlayTool != null)
        {
            return Cursors.Cross;
        }
        int chosen = OverlayLane.SelectedOverlay;
        if (IsOverlayOn(chosen) && HitHandle(_timeline!.Overlays[chosen], point) >= 0)
        {
            return Cursors.Cross;
        }
        return HitOverlay(point) >= 0 ? Cursors.SizeAll : null;
    }
}
