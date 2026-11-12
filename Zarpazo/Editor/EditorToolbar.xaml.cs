using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zarpazo.Output;
using Zarpazo.Recording;

namespace Zarpazo.Editor;

public partial class EditorToolbar : UserControl
{
    // The dialog's 16 custom color slots, kept so colors mixed earlier are still there next time.
    private int[]? _customColors;

    // The panel's own size: what the preview has to fit in.
    private const double PreviewWidth = 256;
    private const double PreviewHeight = 158;

    private RecordingOptions _recording = RecordingOptions.Default;
    private bool _recordingReady; // same reason as _beautifyReady
    private BeautifyStyle _beautify = BeautifyStyle.Default;
    private bool _beautifyEnabled;
    private bool _beautifyReady; // the radio buttons fire Checked while they are being set up and restored

    private readonly List<ToolGroup> _groups = [];
    private List<RadioButton>? _toolRadios; // every button that picks a tool, in the bar and in the group menus
    private bool _applyingTool;
    private Rect _area; // where the bars were last placed
    private Size _screen;
    private PlacementMode _side = PlacementMode.Right;

    public EditorToolbar()
    {
        InitializeComponent();
        _groups.Add(BuildGroup(ShapesSlot, "Formas", AnnotationTool.Arrow,
            new ToolEntry(AnnotationTool.Rectangle, "Rectángulo", "IconRectangle", "Rectángulo"),
            new ToolEntry(AnnotationTool.Ellipse, "Elipse", "IconEllipse", "Elipse"),
            new ToolEntry(AnnotationTool.Line, "Línea", "IconLine", "Línea"),
            new ToolEntry(AnnotationTool.Arrow, "Flecha", "IconArrow", "Flecha")));
        _groups.Add(BuildGroup(DrawSlot, "Dibujo", AnnotationTool.Pen,
            new ToolEntry(AnnotationTool.Pen, "Lápiz", "IconPen", "Lápiz"),
            new ToolEntry(AnnotationTool.Highlighter, "Resaltador", "IconHighlighter", "Resaltador")));
        _groups.Add(BuildGroup(MeasureSlot, "Medir", AnnotationTool.Ruler,
            new ToolEntry(AnnotationTool.Ruler, "Regla", "IconRuler", "Regla: mide una distancia en píxeles"),
            new ToolEntry(AnnotationTool.Picker, "Cuentagotas", "IconPicker", "Cuentagotas: clic copia el color, X cambia entre HEX y RGB")));
        _toolRadios = ToolRadios(this).ToList();
        ApplyTool(AnnotationTool.Select); // shows the groups' buttons and hides the color
        BuildBackgroundChoices();
        ShowBeautify();
        _beautifyReady = true;
        ShowRecordingChoices();
        _recordingReady = true;
    }

    /// <summary>
    /// Puts the tools column and the actions row around the chosen area. The control covers the whole screen but only the two bars
    /// take clicks; everywhere else they reach the window behind.
    /// </summary>
    internal void Place(Rect area, Size screen)
    {
        _area = area;
        _screen = screen;
        Width = screen.Width;
        Height = screen.Height;
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        ToolsBar.Measure(unbounded);
        ActionsBar.Measure(unbounded);

        ToolbarPlacement placement = ToolbarLayout.Place(area, screen, ToolsBar.DesiredSize, ActionsBar.DesiredSize);
        Canvas.SetLeft(ToolsBar, placement.Tools.X);
        Canvas.SetTop(ToolsBar, placement.Tools.Y);
        Canvas.SetLeft(ActionsBar, placement.Actions.X);
        Canvas.SetTop(ActionsBar, placement.Actions.Y);

        // The color pop-up and the group menus open away from the area.
        var side = placement.ToolsOnRight ? System.Windows.Controls.Primitives.PlacementMode.Right : System.Windows.Controls.Primitives.PlacementMode.Left;
        _side = side;
        ColorPopup.Placement = side;
        foreach (ToolGroup group in _groups)
        {
            group.Menu.Placement = side;
        }
    }

    // ---- Tool groups ------------------------------------------------------------------------------------------------------

    private sealed record ToolEntry(AnnotationTool Tool, string Label, string IconKey, string Hint);

    private sealed class ToolGroup
    {
        public required string Title { get; init; }
        public required IReadOnlyList<ToolEntry> Entries { get; init; }
        public required AnnotationTool Remembered { get; set; }
        public required Border Box { get; init; }
        public required ContentControl Icon { get; init; }
        public required Button Face { get; init; }
        public required Popup Menu { get; init; }
        public required Border Corner { get; init; }
    }

    /// <summary>The menu of a group and the corner that opens it; for the tests, which can't point a real mouse.</summary>
    internal (Popup Menu, UIElement Corner) GroupParts(string title)
    {
        ToolGroup group = _groups.First(g => g.Title == title);
        return (group.Menu, group.Corner);
    }

    // One button for tools that belong together: it shows the one used last, a click uses it (or leaves it when it is already
    // in use), and the corner triangle or a right click lists the others.
    private ToolGroup BuildGroup(ContentControl slot, string title, AnnotationTool first, params ToolEntry[] entries)
    {
        var icon = new ContentControl { Focusable = false, IsTabStop = false };
        var face = new Button { Style = (Style)FindResource("Action"), Content = icon, Margin = new Thickness(0) };
        AutomationProperties.SetName(face, title);
        var box = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(1) };

        var triangle = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,6 L6,6 L6,0 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 3),
            IsHitTestVisible = false,
        };
        var corner = new Border
        {
            Width = 14,
            Height = 14,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.Hand,
        };
        box.Child = new Grid { Children = { face, triangle, corner } };

        var list = new StackPanel();
        var rows = new List<(RadioButton Row, ToolEntry Entry)>();
        list.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(OnChoice)); // a pop-up's events don't reach the bar's handler
        foreach (ToolEntry entry in entries)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add((UIElement)FindResource(entry.IconKey));
            label.Children.Add(new TextBlock { Text = entry.Label, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock
            {
                Text = ToolShortcuts.KeyFor(entry.Tool),
                Margin = new Thickness(18, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.55,
            });
            var item = new RadioButton
            {
                GroupName = "tool",
                Tag = entry.Tool.ToString(),
                Style = (Style)FindResource("MenuChoice"),
                ToolTip = ToolShortcuts.WithKey(entry.Hint, entry.Tool),
                Content = label,
            };
            AutomationProperties.SetName(item, entry.Label);
            // The menu opens on press, so the button can also be released over an entry (press, drag, release). Such a release
            // never pressed the entry, so it would not count as a click; a normal click has pressed it and is left alone.
            item.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!item.IsPressed)
                {
                    e.Handled = true;
                    ApplyTool(entry.Tool);
                }
            };
            list.Children.Add(item);
            rows.Add((item, entry));
        }

        var menu = new Popup
        {
            PlacementTarget = box,
            Placement = _side,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x21, 0x24)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Margin = new Thickness(6),
                Child = list,
            },
        };

        var group = new ToolGroup { Title = title, Entries = entries, Remembered = first, Box = box, Icon = icon, Face = face, Menu = menu, Corner = corner };
        face.Click += (_, _) => ApplyTool(group.Entries.Any(entry => entry.Tool == Tool) ? AnnotationTool.Select : group.Remembered);
        face.MouseRightButtonUp += (_, e) =>
        {
            menu.IsOpen = true;
            e.Handled = true;
        };
        // The menu opens on press. Left to close itself on outside clicks it would also close on the release, which lands on the
        // corner and not on the menu, so it stays open until the button is up. The corner keeps the mouse meanwhile so the release
        // is seen wherever it happens: over an entry it picks that tool (press, drag, release); anywhere else the menu stays open
        // for a normal click on an entry.
        corner.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // not a click on the button underneath
            menu.StaysOpen = true;
            menu.IsOpen = true;
            corner.CaptureMouse();
        };
        corner.PreviewMouseLeftButtonUp += (_, e) =>
        {
            corner.ReleaseMouseCapture();
            menu.StaysOpen = false; // from here on it closes on a click outside, like any menu
            (RadioButton Row, ToolEntry Entry) over = rows.FirstOrDefault(r => r.Row.IsVisible && new Rect(r.Row.RenderSize).Contains(e.GetPosition(r.Row)));
            if (over.Row != null)
            {
                ApplyTool(over.Entry.Tool);
            }
            e.Handled = true;
        };

        slot.Content = new Grid { Children = { box, menu } };
        return group;
    }

    private static IEnumerable<RadioButton> ToolRadios(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is RadioButton { GroupName: "tool" } radio)
            {
                yield return radio;
            }
            if (child is DependencyObject node)
            {
                foreach (RadioButton nested in ToolRadios(node))
                {
                    yield return nested;
                }
            }
        }
    }

    // Radio buttons that live in different pop-ups can't see each other, so the checks are kept by hand.
    private void ApplyTool(AnnotationTool tool)
    {
        if (_applyingTool)
        {
            return;
        }

        AnnotationTool previous = Tool;
        _applyingTool = true;
        try
        {
            Tool = tool;
            if (_toolRadios == null)
            {
                return; // still loading the XAML
            }

            foreach (RadioButton radio in _toolRadios)
            {
                radio.IsChecked = (string)radio.Tag == tool.ToString();
            }

            foreach (ToolGroup group in _groups)
            {
                bool used = group.Entries.Any(entry => entry.Tool == tool);
                if (used)
                {
                    group.Remembered = tool;
                }

                ToolEntry shown = group.Entries.First(entry => entry.Tool == group.Remembered);
                group.Icon.Content = FindResource(shown.IconKey);
                group.Box.Background = used ? new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                group.Face.ToolTip = $"{group.Title}: {ToolShortcuts.WithKey(shown.Label, shown.Tool)}. Pulsa otra vez para soltarla; el triángulo o el clic derecho muestran las demás";
                group.Menu.IsOpen = false;
            }

            StyleTools.Visibility = tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Line or AnnotationTool.Arrow
                or AnnotationTool.Pen or AnnotationTool.Highlighter or AnnotationTool.Text or AnnotationTool.Step
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _applyingTool = false;
        }

        if (_screen.Width > 0)
        {
            Place(_area, _screen); // the column got taller or shorter
        }
        if (previous != tool)
        {
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>What the recording panel is set to: the kind of file and the sound sources.</summary>
    internal RecordingOptions RecordingOptions => _recording;

    internal void RestoreRecording(RecordingOptions options)
    {
        _recordingReady = false;
        _recording = options;
        ShowRecordingChoices();
        _recordingReady = true;
    }

    private void ShowRecordingChoices()
    {
        (_recording.Format == RecordingFormat.Video ? RecordVideoChoice : RecordGifChoice).IsChecked = true;
        RecordMicrophoneBox.IsChecked = _recording.Microphone;
        RecordSystemSoundBox.IsChecked = _recording.SystemSound;
        RecordHighlightBox.IsChecked = _recording.HighlightCursor;
        RecordKeysBox.IsChecked = _recording.ShowKeys;
        (_recording.CursorScale >= 1.75 ? RecordCursorHuge : _recording.CursorScale > 1.2 ? RecordCursorLarge : RecordCursorNormal).IsChecked = true;
        // A GIF has no sound, so the boxes only mean something for a video.
        RecordMicrophoneBox.IsEnabled = RecordSystemSoundBox.IsEnabled = RecordMicrophoneList.IsEnabled = RecordSpeakersList.IsEnabled
            = _recording.Format == RecordingFormat.Video;
    }

    private void OnRecordButton(object sender, RoutedEventArgs e)
    {
        FillDevices(); // devices come and go, so the lists are read each time the panel opens
        RecordPopup.IsOpen = true;
    }

    private void FillDevices()
    {
        bool wasReady = _recordingReady;
        _recordingReady = false; // filling a list fires SelectionChanged, which must not overwrite the saved choice
        Fill(RecordMicrophoneList, AudioDevices.Microphones(), _recording.MicrophoneId);
        Fill(RecordSpeakersList, AudioDevices.Speakers(), _recording.SystemSoundId);
        _recordingReady = wasReady;
    }

    private static void Fill(ComboBox list, IReadOnlyList<AudioDeviceInfo> devices, string chosenId)
    {
        var items = new List<AudioDeviceInfo> { new("", "Predeterminado de Windows") };
        items.AddRange(devices);
        list.ItemsSource = items;
        // A device that is no longer connected falls back to the default in the list; it is only replaced once the person picks.
        list.SelectedItem = items.FirstOrDefault(item => item.Id == chosenId) ?? items[0];
    }

    private void OnRecordSpeakersChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_recordingReady && RecordSpeakersList.SelectedItem is AudioDeviceInfo device)
        {
            _recording = _recording with { SystemSoundId = device.Id, SystemSound = true };
            RecordSystemSoundBox.IsChecked = true; // choosing a device means wanting its sound
            RefreshLevelProbes();
        }
    }

    private void OnRecordMicrophoneChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_recordingReady && RecordMicrophoneList.SelectedItem is AudioDeviceInfo device)
        {
            _recording = _recording with { MicrophoneId = device.Id, Microphone = true };
            RecordMicrophoneBox.IsChecked = true; // choosing a microphone means wanting it
            RefreshLevelProbes();
        }
    }

    // While the panel is open, the sources that would be recorded are listened to and shown as level bars, so a microphone
    // that picks up nothing is noticed before recording and not after.
    private AudioLevelProbe? _microphoneProbe;
    private AudioLevelProbe? _speakersProbe;
    private System.Windows.Threading.DispatcherTimer? _probeTimer;

    private void OnRecordPopupOpened(object sender, EventArgs e) => RefreshLevelProbes();

    private void OnRecordPopupClosed(object sender, EventArgs e) => StopLevelProbes();

    private void RefreshLevelProbes()
    {
        StopLevelProbes();
        if (!RecordPopup.IsOpen)
        {
            return;
        }

        bool video = _recording.Format == RecordingFormat.Video;
        _microphoneProbe = video && _recording.Microphone ? AudioLevelProbe.TryOpen(microphone: true, _recording.MicrophoneId) : null;
        _speakersProbe = video && _recording.SystemSound ? AudioLevelProbe.TryOpen(microphone: false, _recording.SystemSoundId) : null;

        MicrophoneLevelTrack.Opacity = _microphoneProbe is null ? 0.35 : 1;
        SpeakersLevelTrack.Opacity = _speakersProbe is null ? 0.35 : 1;
        MicrophoneLevelHint.Text = !video || !_recording.Microphone ? ""
            : _microphoneProbe is null ? "No se pudo abrir este micrófono. Prueba con otro."
            : "Di algo: la barra debe moverse. Si no se mueve, no se está grabando.";

        if (_microphoneProbe is not null || _speakersProbe is not null)
        {
            _probeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _probeTimer.Tick += (_, _) =>
            {
                ShowLevel(MicrophoneLevelTrack, MicrophoneLevelFill, _microphoneProbe);
                ShowLevel(SpeakersLevelTrack, SpeakersLevelFill, _speakersProbe);
            };
            _probeTimer.Start();
        }
    }

    private void StopLevelProbes()
    {
        _probeTimer?.Stop();
        _probeTimer = null;
        _microphoneProbe?.Dispose();
        _microphoneProbe = null;
        _speakersProbe?.Dispose();
        _speakersProbe = null;
        MicrophoneLevelFill.Width = 0;
        SpeakersLevelFill.Width = 0;
    }

    private static void ShowLevel(Border track, Border fill, AudioLevelProbe? probe)
    {
        double fraction = probe is null ? 0 : AudioLevelDisplay.Fraction(probe.Poll());
        fill.Width = track.ActualWidth * fraction;
        fill.Background = AudioLevelDisplay.Color(fraction);
    }

    private void OnRecordChoice(object sender, RoutedEventArgs e)
    {
        if (_recordingReady && e.OriginalSource is RadioButton { GroupName: "recordCursor", Tag: string size })
        {
            _recording = _recording with { CursorScale = double.Parse(size, System.Globalization.CultureInfo.InvariantCulture) };
            return;
        }
        if (!_recordingReady || e.OriginalSource is not RadioButton { GroupName: "recordFormat", Tag: string tag })
        {
            return;
        }

        _recording = _recording with { Format = tag == "video" ? RecordingFormat.Video : RecordingFormat.Gif };
        ShowRecordingChoices();
        RefreshLevelProbes();
    }

    private void OnRecordMicrophone(object sender, RoutedEventArgs e)
    {
        _recording = _recording with { Microphone = RecordMicrophoneBox.IsChecked == true };
        RefreshLevelProbes();
    }

    private void OnRecordHighlight(object sender, RoutedEventArgs e) =>
        _recording = _recording with { HighlightCursor = RecordHighlightBox.IsChecked == true };

    private void OnRecordKeys(object sender, RoutedEventArgs e) =>
        _recording = _recording with { ShowKeys = RecordKeysBox.IsChecked == true };

    private void OnRecordSystemSound(object sender, RoutedEventArgs e)
    {
        _recording = _recording with { SystemSound = RecordSystemSoundBox.IsChecked == true };
        RefreshLevelProbes();
    }

    private void OnRecordStart(object sender, RoutedEventArgs e)
    {
        RecordPopup.IsOpen = false;
        RecordRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The style chosen in the Embellecer panel, whether or not it is switched on.</summary>
    internal BeautifyStyle BeautifyStyle => _beautify;

    /// <summary>True when copying and saving put the capture on the background.</summary>
    public bool BeautifyEnabled => _beautifyEnabled;

    /// <summary>Gives the panel the capture to show on its preview; null when no area is chosen.</summary>
    public Func<BitmapSource?>? PreviewSource { get; set; }

    internal void RestoreBeautify(bool enabled, BeautifyStyle style)
    {
        _beautifyReady = false;
        _beautify = style;
        _beautifyEnabled = enabled;
        ShowBeautify();
        _beautifyReady = true;
    }

    private void BuildBackgroundChoices()
    {
        foreach (BeautifyBackground background in BeautifyBackground.All)
        {
            var swatch = new Ellipse { Width = 18, Height = 18, Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 };
            swatch.Fill = new LinearGradientBrush(background.From, background.To, new Point(0, 0), new Point(1, 1));
            BeautifyBackgrounds.Children.Add(new RadioButton
            {
                GroupName = "beautyBackground",
                Tag = background.Key,
                ToolTip = background.Name,
                Style = (Style)FindResource("Choice"),
                Content = swatch,
            });
        }
    }

    // Puts the current style on the controls: the checked options, the boxes, the dot on the button and the preview.
    private void ShowBeautify()
    {
        FindChoice(this, "beautyBackground", _beautify.Background)?.SetCurrentValue(RadioButton.IsCheckedProperty, true);
        FindChoice(this, "beautyPadding", _beautify.Padding.ToString(CultureInfo.InvariantCulture))?.SetCurrentValue(RadioButton.IsCheckedProperty, true);
        FindChoice(this, "beautyRadius", _beautify.Radius.ToString(CultureInfo.InvariantCulture))?.SetCurrentValue(RadioButton.IsCheckedProperty, true);
        BeautifyShadowBox.IsChecked = _beautify.Shadow;
        BeautifyEnabledBox.IsChecked = _beautifyEnabled;
        ShowBeautifyDot();
        RefreshBeautifyPreview();
    }

    private void ShowBeautifyDot()
    {
        Visibility shown = _beautifyEnabled ? Visibility.Visible : Visibility.Collapsed;
        BeautifyDot.Visibility = shown;
        BeautifyState.Visibility = shown;
    }

    private void RefreshBeautifyPreview()
    {
        BitmapSource? source = PreviewSource?.Invoke();
        BeautifyEmpty.Visibility = source == null ? Visibility.Visible : Visibility.Collapsed;
        if (source == null)
        {
            BeautifyPreview.Source = null;
            return;
        }

        // Drawn small: the whole screenshot at full size would make every click on an option wait.
        double width = source.PixelWidth + 2 * _beautify.Padding;
        double height = source.PixelHeight + 2 * _beautify.Padding;
        double scale = Math.Min(1, Math.Min(PreviewWidth / width, PreviewHeight / height));
        BeautifyPreview.Source = ImageBeautifier.Apply(source, _beautify, scale);
    }

    private void OnBeautifyEdited()
    {
        ShowBeautifyDot();
        RefreshBeautifyPreview();
    }

    private void OnBeautifyButton(object sender, RoutedEventArgs e)
    {
        RefreshBeautifyPreview(); // the area may have changed since the panel was last open
        BeautifyPopup.IsOpen = true;
    }

    private void OnBeautifyEnabled(object sender, RoutedEventArgs e)
    {
        _beautifyEnabled = BeautifyEnabledBox.IsChecked == true;
        OnBeautifyEdited();
    }

    private void OnBeautifyShadow(object sender, RoutedEventArgs e)
    {
        _beautify = _beautify with { Shadow = BeautifyShadowBox.IsChecked == true };
        OnBeautifyEdited();
    }

    private void OnBeautifyChoice(object sender, RoutedEventArgs e)
    {
        if (!_beautifyReady || e.OriginalSource is not RadioButton { Tag: string tag } choice)
        {
            return;
        }

        _beautify = choice.GroupName switch
        {
            "beautyBackground" => _beautify with { Background = tag },
            "beautyPadding" => _beautify with { Padding = int.Parse(tag, CultureInfo.InvariantCulture) },
            "beautyRadius" => _beautify with { Radius = int.Parse(tag, CultureInfo.InvariantCulture) },
            _ => _beautify,
        };
        OnBeautifyEdited();
    }

    // Defaults match the buttons checked in the XAML.
    public AnnotationTool Tool { get; private set; } = AnnotationTool.Select;
    public Color Color { get; private set; } = (Color)ColorConverter.ConvertFromString("#E53935");
    public double Thickness { get; private set; } = StrokeSize.Default;

    /// <summary>Makes the strokes, the text and the step badges bigger or smaller, within the limits.</summary>
    public void SetThickness(double thickness) => Thickness = StrokeSize.Clamp(thickness);

    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    /// <summary>Starts with the color and thickness used last time. Values that make no sense are ignored.</summary>
    public void Restore(string colorHex, double thickness)
    {
        if (FindChoice(this, "color", colorHex) is { } swatch)
        {
            swatch.IsChecked = true;
        }
        else
        {
            try
            {
                ApplyCustomColor((Color)ColorConverter.ConvertFromString(colorHex));
            }
            catch (FormatException)
            {
                // The settings file was edited by hand; keep the default color.
            }
        }

        SetThickness(thickness);
    }

    private static RadioButton? FindChoice(DependencyObject root, string group, string tag)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is RadioButton radio && radio.GroupName == group && string.Equals((string)radio.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return radio;
            }
            if (child is DependencyObject node && FindChoice(node, group, tag) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    public event EventHandler? ToolChanged;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? OcrRequested;
    public event EventHandler? PinRequested;
    public event EventHandler? GuideRequested;
    public event EventHandler? RecordRequested;
    public event EventHandler? CloseRequested;

    // A click on the bar's background must not start a new selection in the window behind it.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => e.Handled = true;

    // This also fires while the XAML loads, before the swatches and popups exist, hence the null checks.
    private void OnChoice(object sender, RoutedEventArgs e)
    {
        // CheckBox raises the same Checked event, and the Embellecer panel has some.
        if (e.OriginalSource is not RadioButton choice)
        {
            return;
        }
        var value = (string)choice.Tag;
        switch (choice.GroupName)
        {
            case "tool":
                ApplyTool(Enum.Parse<AnnotationTool>(value));
                break;
            case "color":
                SetColor((Color)ColorConverter.ConvertFromString(value));
                break;
        }
    }

    // A radio button doesn't uncheck when pressed again, so pressing the active drawing tool goes back to adjusting
    // the area instead. Color choices keep their normal behavior.
    private void OnChoicePressed(object sender, MouseButtonEventArgs e)
    {
        var choice = (RadioButton)sender;
        if (choice.GroupName == "tool" && choice.IsChecked == true && choice != SelectTool)
        {
            e.Handled = true; // also keeps the click from reaching the window behind
            SelectTool.IsChecked = true;
        }
    }

    private void SetColor(Color color)
    {
        Color = color;
        ColorSwatch?.SetCurrentValue(Shape.FillProperty, new SolidColorBrush(color));
        if (ColorPopup != null)
        {
            ColorPopup.IsOpen = false;
        }
    }

    private void OnColorButton(object sender, RoutedEventArgs e) => ColorPopup.IsOpen = true;

    private void OnCustomColor(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(Color.R, Color.G, Color.B),
        };
        if (_customColors != null)
        {
            dialog.CustomColors = _customColors;
        }

        // Owned by the overlay: without an owner the dialog can open behind the always-on-top window.
        var owner = new Win32Owner(new WindowInteropHelper(Window.GetWindow(this)!).Handle);
        if (dialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }
        _customColors = dialog.CustomColors;

        ApplyCustomColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    private void ApplyCustomColor(Color chosen)
    {
        CustomDot.Fill = new SolidColorBrush(chosen);
        CustomChoice.Tag = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
        CustomChoice.Visibility = Visibility.Visible;
        CustomChoice.IsChecked = true;
        SetColor(chosen); // IsChecked doesn't fire again when a second custom color replaces the first
    }

    private void OnUndo(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);

    private void OnRedo(object sender, RoutedEventArgs e) => RedoRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopy(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnSave(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnMoreButton(object sender, RoutedEventArgs e) => MorePopup.IsOpen = true;

    private void OnMoreOcr(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        OnOcr(sender, e);
    }

    private void OnMorePin(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        OnPin(sender, e);
    }

    private void OnMoreGuide(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        GuideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMoreBeautify(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        OnBeautifyButton(sender, e);
    }

    private void OnOcr(object sender, RoutedEventArgs e) => OcrRequested?.Invoke(this, EventArgs.Empty);

    private void OnPin(object sender, RoutedEventArgs e) => PinRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>In group mode Enter adds to the group, so the copy button says its other shortcut.</summary>
    internal void SetGroupMode(bool on) =>
        CopyActionButton.ToolTip = on ? "Copiar al portapapeles (Ctrl+C). Enter añade al grupo (modo grupo)" : "Copiar al portapapeles (Enter)";

    /// <summary>Picks a tool, as its button would.</summary>
    internal void Choose(AnnotationTool tool) => ApplyTool(tool);

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
