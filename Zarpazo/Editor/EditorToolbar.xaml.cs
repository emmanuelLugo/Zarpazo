using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Zarpazo.Editor;

public partial class EditorToolbar : UserControl
{
    // The dialog's 16 custom color slots, kept so colors mixed earlier are still there next time.
    private int[]? _customColors;

    public EditorToolbar() => InitializeComponent();

    // Defaults match the buttons checked in the XAML.
    public AnnotationTool Tool { get; private set; } = AnnotationTool.Select;
    public Color Color { get; private set; } = (Color)ColorConverter.ConvertFromString("#E53935");
    public double Thickness { get; private set; } = 4;

    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? SaveRequested;

    // A click on the bar's background must not start a new selection in the window behind it.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => e.Handled = true;

    // This also fires while the XAML loads, before the swatches and popups exist, hence the null checks.
    private void OnChoice(object sender, RoutedEventArgs e)
    {
        var choice = (RadioButton)e.OriginalSource;
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

        var chosen = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
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
}
