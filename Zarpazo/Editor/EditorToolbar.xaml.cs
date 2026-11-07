using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    public EditorToolbar()
    {
        InitializeComponent();
        BuildBackgroundChoices();
        ShowBeautify();
        _beautifyReady = true;
        ShowRecordingChoices();
        _recordingReady = true;
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
        }
    }

    private void OnRecordMicrophoneChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_recordingReady && RecordMicrophoneList.SelectedItem is AudioDeviceInfo device)
        {
            _recording = _recording with { MicrophoneId = device.Id, Microphone = true };
            RecordMicrophoneBox.IsChecked = true; // choosing a microphone means wanting it
        }
    }

    private void OnRecordChoice(object sender, RoutedEventArgs e)
    {
        if (!_recordingReady || e.OriginalSource is not RadioButton { GroupName: "recordFormat", Tag: string tag })
        {
            return;
        }

        _recording = _recording with { Format = tag == "video" ? RecordingFormat.Video : RecordingFormat.Gif };
        ShowRecordingChoices();
    }

    private void OnRecordMicrophone(object sender, RoutedEventArgs e) =>
        _recording = _recording with { Microphone = RecordMicrophoneBox.IsChecked == true };

    private void OnRecordSystemSound(object sender, RoutedEventArgs e) =>
        _recording = _recording with { SystemSound = RecordSystemSoundBox.IsChecked == true };

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
        BeautifyDot.Visibility = _beautifyEnabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshBeautifyPreview();
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
        BeautifyDot.Visibility = _beautifyEnabled ? Visibility.Visible : Visibility.Collapsed;
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
    public double Thickness { get; private set; } = 4;

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

        FindChoice(this, "size", thickness.ToString(CultureInfo.InvariantCulture))?.SetCurrentValue(RadioButton.IsCheckedProperty, true);
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

    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? OcrRequested;
    public event EventHandler? PinRequested;
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
                Tool = Enum.Parse<AnnotationTool>(value);
                break;
            case "color":
                SetColor((Color)ColorConverter.ConvertFromString(value));
                break;
            case "size":
                Thickness = double.Parse(value, CultureInfo.InvariantCulture);
                if (choice.Content is Ellipse dot && SizeSwatch != null)
                {
                    SizeSwatch.Width = dot.Width;
                    SizeSwatch.Height = dot.Height;
                }
                if (SizePopup != null)
                {
                    SizePopup.IsOpen = false;
                }
                break;
        }
    }

    // A radio button doesn't uncheck when pressed again, so pressing the active drawing tool goes back to adjusting
    // the area instead. Color and size choices keep their normal behavior.
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

    private void OnSizeButton(object sender, RoutedEventArgs e) => SizePopup.IsOpen = true;

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

    private void OnOcr(object sender, RoutedEventArgs e) => OcrRequested?.Invoke(this, EventArgs.Empty);

    private void OnPin(object sender, RoutedEventArgs e) => PinRequested?.Invoke(this, EventArgs.Empty);

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
