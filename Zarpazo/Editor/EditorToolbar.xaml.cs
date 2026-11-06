using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Zarpazo.Editor;

public partial class EditorToolbar : UserControl
{
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
                Color = (Color)ColorConverter.ConvertFromString(value);
                break;
            case "size":
                Thickness = double.Parse(value, CultureInfo.InvariantCulture);
                break;
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);

    private void OnRedo(object sender, RoutedEventArgs e) => RedoRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopy(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnSave(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);
}
