using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zarpazo.Editor;
using Zarpazo.History;
using Zarpazo.Ocr;
using Zarpazo.Output;
using Zarpazo.Pin;
using Zarpazo.Recording;
using Zarpazo.Settings;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Overlay;

public partial class OverlayWindow : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const int DwmwaTransitionsForceDisabled = 3;
    private const int DwmwaCloak = 13;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private const double ToolbarGap = 6;
    private const double ClickTolerance = 4; // a release this close to the press counts as a click, not a drag
    private const double HandleTolerance = 6; // how close to an edge the mouse grabs it
    private const double HandleSize = 7;
    private const double HitTolerance = 6; // how close to an annotation a click picks it
    private const int MinRecordingSide = 16; // pixels; anything smaller is not worth a GIF

    private readonly BitmapSource _screenshot;
    private readonly Rectangle _bounds;
    private readonly IReadOnlyList<Rectangle> _windows;
    private readonly SettingsStore _settings;
    private readonly CaptureHistory _history;
    private readonly EditRestore? _restore;
    private Rect? _hoverRect;   // the window under the cursor, while no area has been chosen yet
    private Rect? _pendingWindow; // that window as it was when the button went down
    private Point _dragStart;
    private bool _replacePending; // button is down outside the chosen area; becomes a new selection only if dragged
    private Annotation? _drawing;
    private SelectionHandle _dragHandle;  // the grab point being dragged to resize or move the capture area
    private Rect _dragOrigin;             // the area as it was when that drag started
    private Annotation? _moving;          // the annotation being dragged with the move tool
    private Point _moveLast;
    private Vector _moveTotal;
    private Annotation? _resizing;        // the annotation being stretched by one of its grips
    private SelectionHandle _resizeGrip;
    private Rect _resizeFrom;             // its box when the drag began
    private Point _resizeStart;
    private bool _recognizing;           // a text reading is in progress
    private DispatcherTimer? _noticeTimer;
    private TextBox? _textBox;
    private TextAnnotation? _editingText;  // the text on the canvas that the box is retyping, if any
    private Point _textOrigin;
    private Color _textColor;
    private double _textThickness;

    /// <param name="screenshot">The virtual screen, one image pixel per physical pixel.</param>
    /// <param name="bounds">The virtual screen in physical pixels; its origin can be negative.</param>
    /// <param name="windows">Window bounds in physical pixels at the moment of the capture, topmost first.</param>
    /// <param name="restore">Set when an earlier capture is opened again: its area and annotations are put back.</param>
    /// <param name="initialArea">An area, in physical screen pixels, to start with: the whole monitor, or the last one captured.</param>
    internal OverlayWindow(BitmapSource screenshot, Rectangle bounds, IReadOnlyList<Rectangle> windows, SettingsStore settings, CaptureHistory history, EditRestore? restore = null, Rectangle? initialArea = null)
    {
        InitializeComponent();
        _history = history;
        _restore = restore;
        if (restore != null)
        {
            Loaded += (_, _) => RestoreEdit(restore); // the layout size is known by now
        }
        _screenshot = screenshot;
        _bounds = bounds;
        _windows = windows;
        _settings = settings;
        if (initialArea is { } area)
        {
            Loaded += (_, _) => SelectArea(area);
        }
        if (settings.Current.RememberStyle)
        {
            Toolbar.Restore(settings.Current.Color, settings.Current.Thickness);
        }
        Toolbar.RestoreBeautify(settings.Current.BeautifyEnabled, settings.Current.BeautifyStyle);
        Toolbar.RestoreRecording(settings.Current.RecordingOptions);
        Toolbar.PreviewSource = () =>
        {
            CommitText(); // so the preview matches what copying would give
            return CropSelection()?.Final;
        };
        Closed += (_, _) => RememberStyle();
        Screenshot.Source = screenshot;
        SelectionBorder.Data = Selection;
        Selection.Changed += (_, _) => UpdateSizeLabel(); // every way the area changes goes through the rectangle
        SizeChanged += (_, e) => DimArea.Rect = new Rect(e.NewSize);

        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(IgnoreDpiChange);

        // WPF paints a blank frame when a window first shows, so keep it invisible until the screenshot is drawn.
        SetDwmFlag(hwnd, DwmwaTransitionsForceDisabled, true);
        SetDwmFlag(hwnd, DwmwaCloak, true);
        ContentRendered += (_, _) =>
        {
            SetDwmFlag(hwnd, DwmwaCloak, false);
            Activate();
        };

        // Physical pixels: WPF's Left/Top/Width/Height can't describe a window spanning monitors with different DPI.
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape)
        {
            CancelStep();
        }
        else if (e.Key == Key.Enter || (ctrl && e.Key == Key.C))
        {
            OnCopy(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.S)
        {
            OnSave(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.T)
        {
            OnOcr(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.P)
        {
            OnPin(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.G)
        {
            OnRecord(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.Z)
        {
            OnUndo(this, EventArgs.Empty);
        }
        else if (ctrl && e.Key == Key.Y)
        {
            OnRedo(this, EventArgs.Empty);
        }
        else if (e.Key == Key.Delete)
        {
            Annotations.DeleteSelected();
        }
        else if (e.Key == Key.X && Toolbar.Tool == AnnotationTool.Picker)
        {
            Probe.ToggleFormat();
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && !IsSelectionFinal)
        {
            // Fine adjustment while choosing the area: the cursor moves one screen pixel, which also moves the magnifier.
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(
                cursor.X + (e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0),
                cursor.Y + (e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0));
            e.Handled = true;
        }
        else if (!ctrl && Keyboard.Modifiers == ModifierKeys.None && IsSelectionFinal && _textBox == null && !IsMouseCaptured
                 && ToolShortcuts.ForKey(e.Key) is { } shortcut)
        {
            Toolbar.Choose(shortcut); // the letters only pick a tool once the area is chosen; before that F and the arrows have their uses
        }
        else if (e.Key == Key.F && !ctrl && _textBox == null)
        {
            SelectArea(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).Bounds); // F: the whole monitor
        }
    }

    /// <summary>
    /// Esc undoes one step at a time: first the annotation picked with the move tool, then the tool in use (back to adjusting the
    /// area), and only then it closes without saving. Work drawn is not lost to a single stray press.
    /// </summary>
    private void CancelStep()
    {
        if (Annotations.Selected != null)
        {
            Annotations.Selected = null;
        }
        else if (IsSelectionFinal && Toolbar.Tool != AnnotationTool.Select)
        {
            Toolbar.Choose(AnnotationTool.Select);
        }
        else
        {
            Close();
        }
    }

    /// <summary>Chooses an area given in physical screen pixels, as if it had been dragged, and shows the bars.</summary>
    private void SelectArea(Rectangle pixels)
    {
        double scaleX = _screenshot.PixelWidth / ActualWidth;
        double scaleY = _screenshot.PixelHeight / ActualHeight;
        Rect area = SelectionGeometry.FromScreenPixels(pixels, _bounds, scaleX, scaleY);
        if (area.IsEmpty)
        {
            return;
        }

        CommitText();
        Annotations.Clear(); // like dragging a new area: what was drawn belonged to the old one
        _hoverRect = null;
        Selection.Rect = area;
        Annotations.ClipArea = area;
        ShowToolbar();
    }

    /// <summary>An area was copied, saved, pinned or recorded: its place in physical screen pixels, so it can be repeated.</summary>
    internal event Action<Rectangle>? AreaUsed;

    /// <summary>The capture was also saved by itself (the path); raised after the overlay has done its work.</summary>
    internal event Action<string>? AutoSaved;

    /// <summary>The capture was copied but could not be saved by itself (the reason).</summary>
    internal event Action<string>? AutoSaveFailed;

    private void OnCopy(object? sender, EventArgs e) =>
        Finish(image =>
        {
            BitmapSource final = Embellished(image);
            ImageOutput.CopyToClipboard(final, _settings.Current.CopyAsFile);
            AutoSave(final);
            return true;
        });

    // After the copy: if automatic saving is on the same picture is also written to the folder. A problem there must not undo the copy.
    private void AutoSave(BitmapSource image)
    {
        var saver = new AutoSaver(_settings);
        if (!saver.IsEnabled)
        {
            return;
        }

        try
        {
            AutoSaved?.Invoke(saver.SaveImage(image));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AutoSaveFailed?.Invoke(ex.Message);
        }
    }

    private void OnSave(object? sender, EventArgs e) => Finish(image => ImageOutput.Save(Embellished(image), this, _settings.Current));

    // Only what leaves the app is dressed up. The history, the pinned copy and the text reading keep the plain capture.
    private BitmapSource Embellished(BitmapSource image) =>
        Toolbar.BeautifyEnabled ? ImageBeautifier.Apply(image, Toolbar.BeautifyStyle) : image;

    /// <summary>Leaves the capture floating over the screen, exactly where the area was.</summary>
    private void OnPin(object? sender, EventArgs e) => FinishWithCrop(crop =>
    {
        // No owner: a pinned capture has to outlive this window.
        new PinnedWindow(crop.Final, _bounds.X + crop.Pixels.X, _bounds.Y + crop.Pixels.Y, _settings).Show();
        return true;
    });

    /// <summary>Asked to record the chosen area, in physical screen pixels, the way the recording panel is set.</summary>
    internal event Action<Rectangle, RecordingOptions>? RecordRequested;

    private void OnRecord(object? sender, EventArgs e)
    {
        CommitText();
        Crop? crop = CropSelection();
        if (crop == null)
        {
            return;
        }
        if (crop.Pixels.Width < MinRecordingSide || crop.Pixels.Height < MinRecordingSide)
        {
            ShowNotice("La zona es muy pequeña para grabar");
            return;
        }

        // The overlay has to be gone before the first picture, or it would be in every frame of the recording.
        var region = new Rectangle(_bounds.X + crop.Pixels.X, _bounds.Y + crop.Pixels.Y, crop.Pixels.Width, crop.Pixels.Height);
        AreaUsed?.Invoke(region);
        RecordRequested?.Invoke(region, Toolbar.RecordingOptions);
        Close();
    }

    /// <summary>Reads the text in the chosen area and copies it. The overlay stays open so the area can be adjusted and tried again.</summary>
    private async void OnOcr(object? sender, EventArgs e)
    {
        if (_recognizing)
        {
            return;
        }

        CommitText();
        Crop? crop = CropSelection();
        if (crop == null)
        {
            return;
        }

        _recognizing = true;
        ShowNotice("Leyendo texto…", TimeSpan.FromSeconds(30));
        try
        {
            // The pixels as they were on the screen: drawn annotations would get in the way of the reading.
            string text = await TextRecognizer.RecognizeAsync(crop.Clean);
            if (text.Length == 0)
            {
                ShowNotice("No se encontró texto");
                return;
            }

            Clipboard.SetDataObject(text, copy: true);
            ShowNotice($"Texto copiado ({text.Length} caracteres)");
        }
        catch (InvalidOperationException ex)
        {
            ShowNotice(ex.Message, TimeSpan.FromSeconds(5)); // no OCR language installed
        }
        catch (COMException)
        {
            ShowNotice("No se pudo copiar"); // another program holds the clipboard
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowNotice("No se pudo leer el texto");
        }
        finally
        {
            _recognizing = false;
        }
    }

    /// <summary>A short message in the middle of the chosen area.</summary>
    private void ShowNotice(string message, TimeSpan? duration = null)
    {
        NoticeText.Text = message;
        Notice.Visibility = Visibility.Visible;
        Notice.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = Notice.DesiredSize;
        Rect area = Selection.Rect;
        Canvas.SetLeft(Notice, Math.Clamp(area.X + (area.Width - size.Width) / 2, 0, Math.Max(0, ActualWidth - size.Width)));
        Canvas.SetTop(Notice, Math.Clamp(area.Y + (area.Height - size.Height) / 2, 0, Math.Max(0, ActualHeight - size.Height)));

        _noticeTimer?.Stop();
        _noticeTimer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(2.5) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer?.Stop();
            Notice.Visibility = Visibility.Collapsed;
        };
        _noticeTimer.Start();
    }

    // Written when the overlay closes, so the next capture starts with the last color and thickness used.
    private void RememberStyle()
    {
        AppSettings settings = _settings.Current;
        bool changed = false;
        if (settings.RememberStyle && (settings.Color != Toolbar.ColorHex || settings.Thickness != Toolbar.Thickness))
        {
            settings.Color = Toolbar.ColorHex;
            settings.Thickness = Toolbar.Thickness;
            changed = true;
        }
        if (settings.BeautifyEnabled != Toolbar.BeautifyEnabled || settings.BeautifyStyle != Toolbar.BeautifyStyle)
        {
            settings.BeautifyEnabled = Toolbar.BeautifyEnabled;
            settings.BeautifyStyle = Toolbar.BeautifyStyle;
            changed = true;
        }
        if (settings.RecordingOptions != Toolbar.RecordingOptions)
        {
            settings.RecordingOptions = Toolbar.RecordingOptions;
            changed = true;
        }
        if (!changed)
        {
            return;
        }

        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a color preference isn't worth interrupting the person.
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnUndo(object? sender, EventArgs e) => Annotations.Undo();

    private void OnRedo(object? sender, EventArgs e) => Annotations.Redo();

    /// <summary>Runs the output on the selected area and closes. Stays open if there is no selection, the user cancels or it fails.</summary>
    private void Finish(Func<BitmapSource, bool> output) => FinishWithCrop(crop => output(crop.Final));

    private void FinishWithCrop(Func<Crop, bool> output)
    {
        CommitText(); // text still being typed belongs in the result
        Crop? crop = CropSelection();
        if (crop == null)
        {
            return;
        }

        try
        {
            if (!output(crop))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AddToHistory(crop);
        AreaUsed?.Invoke(new Rectangle(_bounds.X + crop.Pixels.X, _bounds.Y + crop.Pixels.Y, crop.Pixels.Width, crop.Pixels.Height));
        Close();
    }

    // The capture is already copied or saved, so a full disk or a locked folder must not turn that into an error.
    private void AddToHistory(Crop crop)
    {
        if (!_settings.Current.KeepHistory)
        {
            return;
        }

        try
        {
            // Screen pixels, so the annotations stay valid even if the screens are arranged differently next time.
            Point ToScreenPixels(Point point) => new(_bounds.X + point.X * crop.ScaleX, _bounds.Y + point.Y * crop.ScaleY);

            // Pixelate hides data that must not be recoverable, so a capture that uses it keeps only the finished
            // picture (still editable on top of it, but the pixelated area can't be undone).
            bool restorable = Annotations.Items.All(AnnotationData.CanRestore);
            var state = new EditState
            {
                Left = _bounds.X + crop.Pixels.X,
                Top = _bounds.Y + crop.Pixels.Y,
                Width = crop.Pixels.Width,
                Height = crop.Pixels.Height,
                Annotations = restorable
                    ? Annotations.Items.Select(item => AnnotationData.From(item, ToScreenPixels, crop.ScaleX)).ToList()
                    : [],
            };
            BitmapSource? clean = restorable && state.Annotations.Count > 0 ? crop.Clean : null;

            _history.Add(crop.Final, _settings.Current.HistoryLimit, state, clean);
            _history.DeleteOlderThan(_settings.Current.HistoryMaxAgeDays, DateTime.Now);
            if (_restore != null)
            {
                _history.Delete(_restore.ReplacesPath); // the edited capture takes the place of the one it came from
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Puts back the area and the annotations of a capture that is being edited again.</summary>
    private void RestoreEdit(EditRestore restore)
    {
        // Image pixels per layout unit; the window may be scaled for DPI, so don't assume 1.
        double scaleX = _screenshot.PixelWidth / ActualWidth;
        double scaleY = _screenshot.PixelHeight / ActualHeight;
        int shiftX = restore.Left - restore.State.Left; // not zero if the capture had to be moved to fit the screens
        int shiftY = restore.Top - restore.State.Top;
        Point ToLayout(Point screenPixel) => new((screenPixel.X + shiftX - _bounds.X) / scaleX, (screenPixel.Y + shiftY - _bounds.Y) / scaleY);

        Selection.Rect = new Rect(
            ToLayout(new Point(restore.State.Left, restore.State.Top)),
            new Size(restore.State.Width / scaleX, restore.State.Height / scaleY));
        Annotations.ClipArea = Selection.Rect;

        try
        {
            var screen = new ScreenSource(_screenshot, scaleX, scaleY);
            Annotations.Load(restore.State.Annotations.Select(data => data.ToAnnotation(ToLayout, scaleX, screen)).ToList());
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            // A damaged annotation: the picture still opens, just without the annotations.
        }
        ShowToolbar();
    }

    /// <summary>The selected area as it was on the screen, and with the annotations drawn on it.</summary>
    private sealed record Crop(BitmapSource Clean, BitmapSource Final, Int32Rect Pixels, double ScaleX, double ScaleY);

    private Crop? CropSelection()
    {
        if (!HasSelection)
        {
            return null;
        }

        Rect area = Selection.Rect;
        // Image pixels per layout unit; the window may be scaled for DPI, so don't assume 1.
        double scaleX = _screenshot.PixelWidth / ActualWidth;
        double scaleY = _screenshot.PixelHeight / ActualHeight;
        int x = Math.Clamp((int)Math.Round(area.X * scaleX), 0, _screenshot.PixelWidth - 1);
        int y = Math.Clamp((int)Math.Round(area.Y * scaleY), 0, _screenshot.PixelHeight - 1);
        int width = Math.Clamp((int)Math.Round(area.Width * scaleX), 0, _screenshot.PixelWidth - x);
        int height = Math.Clamp((int)Math.Round(area.Height * scaleY), 0, _screenshot.PixelHeight - y);
        if (width < 2 || height < 2)
        {
            return null; // a plain click, not a selection
        }

        var crop = new CroppedBitmap(_screenshot, new Int32Rect(x, y, width, height));
        crop.Freeze();
        return new Crop(crop, Annotations.Compose(crop, x, y, scaleX, scaleY), new Int32Rect(x, y, width, height), scaleX, scaleY);
    }

    // A click without dragging leaves a zero-size rectangle, which is not a selection.
    // A highlighted window is only a preview until it is clicked.
    private bool HasSelection => _hoverRect == null && Selection.Rect is { IsEmpty: false, Width: >= 2, Height: >= 2 };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_textBox is { IsMouseOver: true })
        {
            return; // clicking inside the text being typed
        }
        CommitText();

        Point point = e.GetPosition(this);
        if (Toolbar.Tool == AnnotationTool.Picker)
        {
            CopyProbeColor();
            return;
        }
        if (Toolbar.Tool != AnnotationTool.Move)
        {
            Annotations.Selected = null;
        }

        SelectionHandle handle = Toolbar.Tool == AnnotationTool.Select && IsSelectionFinal
            ? SelectionGeometry.HitTest(Selection.Rect, point, HandleTolerance)
            : SelectionHandle.None;
        if (handle != SelectionHandle.None)
        {
            // Resize or move the area that is already chosen; the annotations stay where they are.
            _dragHandle = handle;
            _dragOrigin = Selection.Rect;
            _dragStart = point;
        }
        else if (Toolbar.Tool == AnnotationTool.Select)
        {
            _dragStart = point;
            _pendingWindow = _hoverRect;
            _hoverRect = null;
            if (IsSelectionFinal)
            {
                _replacePending = true; // a plain click outside keeps the current area; only a drag replaces it
            }
            else
            {
                StartNewSelection(point);
            }
        }
        else if (Toolbar.Tool == AnnotationTool.Move && Annotations.Selected is { } picked
                 && Annotations.HandleAt(point, HandleTolerance) is var grip && grip != SelectionHandle.None)
        {
            // A grip of the picked annotation: stretch it instead of moving it.
            _resizing = picked;
            _resizeGrip = grip;
            _resizeFrom = picked.Frame;
            _resizeStart = point;
        }
        else if (HasSelection && Selection.Rect.Contains(point))
        {
            if (Toolbar.Tool == AnnotationTool.Move)
            {
                _moving = Annotations.HitTest(point, HitTolerance);
                Annotations.Selected = _moving;
                if (_moving == null)
                {
                    return;
                }
                if (_moving is TextAnnotation words && e.ClickCount == 2)
                {
                    _moving = null; // a double click retypes the text instead of dragging it
                    BeginText(words.Origin, words);
                    return;
                }
                _moveLast = point;
                _moveTotal = default;
            }
            else if (Toolbar.Tool == AnnotationTool.Text)
            {
                // A click on a text that is already there retypes it instead of starting another one on top.
                if (Annotations.HitTest(point, HitTolerance) is TextAnnotation existing)
                {
                    BeginText(existing.Origin, existing);
                }
                else
                {
                    BeginText(point);
                }
                return;
            }
            else if (Toolbar.Tool == AnnotationTool.Step)
            {
                Annotations.Commit(new NumberAnnotation(Toolbar.Color, Toolbar.Thickness, point, Annotations.NextStepNumber));
                return;
            }
            else
            {
                var screen = new ScreenSource(_screenshot, _screenshot.PixelWidth / ActualWidth, _screenshot.PixelHeight / ActualHeight);
                _drawing = Annotation.Create(Toolbar.Tool, Toolbar.Color, Toolbar.Thickness, point, screen);
                Annotations.Begin(_drawing);
            }
        }
        else
        {
            return; // drawing tools ignore clicks outside the capture area
        }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            HoverWindow(e.GetPosition(this));
            UpdateCursor(e.GetPosition(this));
            UpdateProbe(e.GetPosition(this));
            return;
        }

        Point point = e.GetPosition(this);
        if (_drawing != null)
        {
            Rect area = Selection.Rect;
            _drawing.Extend(new Point(Math.Clamp(point.X, area.Left, area.Right), Math.Clamp(point.Y, area.Top, area.Bottom)));
            Annotations.InvalidateVisual();
        }
        else if (_resizing != null)
        {
            Rect frame = SelectionGeometry.Stretch(_resizeFrom, _resizeGrip, point - _resizeStart, Selection.Rect);
            _resizing.Resize(_resizing.Frame, frame);
            Annotations.InvalidateVisual();
        }
        else if (_moving != null)
        {
            Vector step = point - _moveLast;
            _moving.Offset(step);
            _moveLast = point;
            _moveTotal += step;
            Annotations.InvalidateVisual();
        }
        else if (_dragHandle != SelectionHandle.None)
        {
            Selection.Rect = SelectionGeometry.Apply(_dragOrigin, _dragHandle, point - _dragStart, new Rect(0, 0, ActualWidth, ActualHeight));
            Annotations.ClipArea = Selection.Rect;
            ShowToolbar();
        }
        else
        {
            if (_replacePending && (point - _dragStart).Length >= ClickTolerance)
            {
                _replacePending = false;
                StartNewSelection(_dragStart);
            }
            if (!_replacePending)
            {
                Selection.Rect = new Rect(_dragStart, point);
            }
            UpdateProbe(point);
        }
    }

    private void StartNewSelection(Point origin)
    {
        Annotations.Clear();
        Toolbar.Visibility = Visibility.Collapsed;
        Handles.Visibility = Visibility.Collapsed;
        Selection.Rect = new Rect(origin, origin);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        // Releasing the capture makes WPF send a mouse move right away, which would run HoverWindow and replace the
        // selection with the window underneath. So it is released last, once the selection and toolbar are in place.
        try
        {
            FinishDrag(e);
        }
        finally
        {
            ReleaseMouseCapture();
        }
    }

    private void FinishDrag(MouseButtonEventArgs e)
    {
        if (_drawing != null)
        {
            Annotations.Commit(_drawing);
            _drawing = null;
        }
        else if (_resizing != null)
        {
            Annotations.RecordResize(_resizing, _resizeFrom, _resizing.Frame);
            _resizing = null;
        }
        else if (_moving != null)
        {
            Annotations.RecordMove(_moving, _moveTotal);
            _moving = null;
        }
        else if (_dragHandle != SelectionHandle.None)
        {
            _dragHandle = SelectionHandle.None; // the area was already updated while dragging
        }
        else if (_replacePending)
        {
            _replacePending = false; // it was only a click: the area stays as it was
        }
        else
        {
            if (_pendingWindow is { } window && (e.GetPosition(this) - _dragStart).Length < ClickTolerance)
            {
                Selection.Rect = window;
            }
            _pendingWindow = null;

            if (HasSelection)
            {
                Annotations.ClipArea = Selection.Rect;
                ShowToolbar();
            }
        }
    }

    // The toolbar only shows once an area has been chosen.
    private bool IsSelectionFinal => Toolbar.Visibility == Visibility.Visible;

    /// <summary>Shows what a click would do here: resize, move, or start a new selection.</summary>
    private void UpdateCursor(Point point)
    {
        if (!IsSelectionFinal)
        {
            Cursor = Cursors.Cross;
            return;
        }

        Cursor = Toolbar.Tool switch
        {
            AnnotationTool.Select => SelectionGeometry.HitTest(Selection.Rect, point, HandleTolerance) switch
            {
                SelectionHandle.Inside => Cursors.SizeAll,
                SelectionHandle.North or SelectionHandle.South => Cursors.SizeNS,
                SelectionHandle.East or SelectionHandle.West => Cursors.SizeWE,
                SelectionHandle.NorthWest or SelectionHandle.SouthEast => Cursors.SizeNWSE,
                SelectionHandle.NorthEast or SelectionHandle.SouthWest => Cursors.SizeNESW,
                _ => Cursors.Cross,
            },
            AnnotationTool.Move => Annotations.HandleAt(point, HandleTolerance) switch
            {
                SelectionHandle.North or SelectionHandle.South => Cursors.SizeNS,
                SelectionHandle.East or SelectionHandle.West => Cursors.SizeWE,
                SelectionHandle.NorthWest or SelectionHandle.SouthEast => Cursors.SizeNWSE,
                SelectionHandle.NorthEast or SelectionHandle.SouthWest => Cursors.SizeNESW,
                _ => Selection.Rect.Contains(point) && Annotations.HitTest(point, HitTolerance) != null ? Cursors.SizeAll : Cursors.Arrow,
            },
            _ => Cursors.Cross,
        };
    }

    private static IEnumerable<Rectangle> MonitorBounds => System.Windows.Forms.Screen.AllScreens.Select(screen => screen.Bounds);

    /// <summary>Highlights the window under the cursor, until an area has been chosen.</summary>
    private void HoverWindow(Point point)
    {
        if (IsSelectionFinal)
        {
            return;
        }

        // Image pixels per layout unit, which is also how window pixels map onto the overlay.
        double scaleX = _screenshot.PixelWidth / ActualWidth;
        double scaleY = _screenshot.PixelHeight / ActualHeight;
        int x = _bounds.X + (int)Math.Round(point.X * scaleX);
        int y = _bounds.Y + (int)Math.Round(point.Y * scaleY);

        foreach (Rectangle window in _windows)
        {
            Rectangle visible = Rectangle.Intersect(window, _bounds); // maximized windows reach past the screen edge
            if (!visible.Contains(x, y))
            {
                continue;
            }

            // This one is on top here. If it fills the monitor, nothing is highlighted: carrying on down the list would
            // outline a window that is hidden behind it.
            if (SelectionGeometry.FillsAMonitor(visible, MonitorBounds))
            {
                break;
            }

            _hoverRect = new Rect(
                (visible.X - _bounds.X) / scaleX,
                (visible.Y - _bounds.Y) / scaleY,
                visible.Width / scaleX,
                visible.Height / scaleY);
            Selection.Rect = _hoverRect.Value;
            return;
        }

        _hoverRect = null;
        Selection.Rect = Rect.Empty;
    }

    /// <summary>The "width × height" tag above the area, in image pixels.</summary>
    private void UpdateSizeLabel()
    {
        Rect area = Selection.Rect;
        if (area.IsEmpty || area.Width < 1 || area.Height < 1 || ActualWidth == 0)
        {
            SizeLabel.Visibility = Visibility.Collapsed;
            return;
        }

        double scaleX = _screenshot.PixelWidth / ActualWidth;
        double scaleY = _screenshot.PixelHeight / ActualHeight;
        SizeText.Text = $"{Math.Round(area.Width * scaleX)} × {Math.Round(area.Height * scaleY)}";
        SizeLabel.Visibility = Visibility.Visible;
        SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = SizeLabel.DesiredSize;

        // Above the area; inside its corner when it touches the top of the screen.
        double top = area.Top - size.Height - ToolbarGap;
        Canvas.SetTop(SizeLabel, top < 0 ? area.Top + ToolbarGap : top);
        Canvas.SetLeft(SizeLabel, Math.Clamp(area.Left, 0, Math.Max(0, ActualWidth - size.Width)));
    }

    /// <summary>
    /// Shows the magnifier next to the cursor: with the screen position while an area is being chosen, so its edges can be
    /// placed pixel by pixel, and with the color while the color picker is the active tool.
    /// </summary>
    private void UpdateProbe(Point point)
    {
        bool choosing = !IsSelectionFinal;
        if (!choosing && Toolbar.Tool != AnnotationTool.Picker)
        {
            Probe.Visibility = Visibility.Collapsed;
            return;
        }

        int pixelX = (int)Math.Floor(point.X * _screenshot.PixelWidth / ActualWidth);
        int pixelY = (int)Math.Floor(point.Y * _screenshot.PixelHeight / ActualHeight);
        Probe.Show(_screenshot, pixelX, pixelY, choosing ? $"X {_bounds.X + pixelX}  Y {_bounds.Y + pixelY}" : null);
        Probe.Visibility = Visibility.Visible;
        Probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = Probe.DesiredSize;

        const double gap = 20;
        double left = point.X + gap + size.Width > ActualWidth ? point.X - gap - size.Width : point.X + gap;
        double top = point.Y + gap + size.Height > ActualHeight ? point.Y - gap - size.Height : point.Y + gap;
        Canvas.SetLeft(Probe, Math.Max(0, left));
        Canvas.SetTop(Probe, Math.Max(0, top));
    }

    private void CopyProbeColor()
    {
        try
        {
            Clipboard.SetText(Probe.Text);
            Probe.ShowNotice($"Copiado {Probe.Text}");
        }
        catch (COMException)
        {
            Probe.ShowNotice("No se pudo copiar"); // another program holds the clipboard
        }
    }

    /// <param name="editing">A text already on the canvas to type over; it keeps its color and size and stays out of sight meanwhile.</param>
    private void BeginText(Point point, TextAnnotation? editing = null)
    {
        _textColor = editing?.Color ?? Toolbar.Color;
        _textThickness = editing?.Thickness ?? Toolbar.Thickness;
        var brush = new SolidColorBrush(_textColor);
        _textOrigin = point;
        _editingText = editing;
        Annotations.Hidden = editing;
        _textBox = new TextBox
        {
            Text = editing?.Text ?? "",
            Foreground = brush,
            CaretBrush = brush,
            FontFamily = TextAnnotation.Font,
            FontWeight = TextAnnotation.Weight,
            FontSize = TextAnnotation.SizeFor(_textThickness),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 60,
            AcceptsReturn = true,
            Cursor = Cursors.IBeam,
        };
        _textBox.PreviewKeyDown += OnTextKeyDown;
        _textBox.Loaded += (_, _) => // can't take focus before it has been laid out
        {
            _textBox?.Focus();
            _textBox?.Select(_textBox.Text.Length, 0); // typing continues at the end of what was written
        };
        // The 1px border sits outside the text, so shift the box to keep the text exactly where it will be drawn.
        Canvas.SetLeft(_textBox, point.X - 1);
        Canvas.SetTop(_textBox, point.Y - 1);
        UiLayer.Children.Add(_textBox);
    }

    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RemoveTextBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitText();
            e.Handled = true;
        }
    }

    private void CommitText()
    {
        if (_textBox == null)
        {
            return;
        }

        string text = _textBox.Text.TrimEnd();
        TextAnnotation? editing = _editingText;
        RemoveTextBox();

        if (editing == null)
        {
            Annotations.Commit(new TextAnnotation(_textColor, _textThickness, _textOrigin, text));
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            Annotations.Selected = editing; // emptied: the same as deleting it, and undo brings it back
            Annotations.DeleteSelected();
        }
        else
        {
            Annotations.EditText(editing, text);
        }
    }

    private void RemoveTextBox()
    {
        UiLayer.Children.Remove(_textBox);
        _textBox = null;
        _editingText = null;
        Annotations.Hidden = null;
        Focus(); // otherwise Esc and the shortcuts stop reaching the window
    }

    private void ShowToolbar()
    {
        Rect area = Selection.Rect;
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.Place(area, new Size(ActualWidth, ActualHeight)); // the tools beside the area, the actions under it
        UpdateHandles(area);
    }

    private void UpdateHandles(Rect area)
    {
        double middleX = area.X + area.Width / 2;
        double middleY = area.Y + area.Height / 2;
        var handles = new GeometryGroup();
        foreach (double x in new[] { area.Left, middleX, area.Right })
        {
            foreach (double y in new[] { area.Top, middleY, area.Bottom })
            {
                if (x != middleX || y != middleY)
                {
                    handles.Children.Add(new RectangleGeometry(new Rect(x - HandleSize / 2, y - HandleSize / 2, HandleSize, HandleSize)));
                }
            }
        }
        Handles.Data = handles;
        Handles.Visibility = Visibility.Visible;
    }

    // Windows asks to rescale the window when its main monitor changes, which would break the 1:1 pixel fit.
    private static IntPtr IgnoreDpiChange(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void SetDwmFlag(IntPtr hwnd, int attribute, bool value)
    {
        int flag = value ? 1 : 0;
        DwmSetWindowAttribute(hwnd, attribute, ref flag, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
