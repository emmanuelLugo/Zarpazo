using System.IO;
using System.Windows;
using Zarpazo.Theme;

namespace Zarpazo.Guide;

public partial class GuideWindow : Window
{
    private readonly GuideDocument _guide;

    internal GuideWindow(GuideDocument guide)
    {
        InitializeComponent();
        _guide = guide;
        TitleBarTheme.Follow(this);
        Reload();
    }

    /// <summary>Shows the captures again; the order or the number of them changed.</summary>
    internal void Reload()
    {
        Items.ItemsSource = null; // forces the list to draw the new numbers
        Items.ItemsSource = _guide.Steps;
        bool any = _guide.Steps.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = any;
        ClearButton.IsEnabled = any;
        StatusText.Text = any ? $"{_guide.Steps.Count} {(_guide.Steps.Count == 1 ? "captura" : "capturas")}" : "";
    }

    private static GuideStep Step(object sender) => (GuideStep)((FrameworkElement)sender).DataContext;

    private void OnMoveUp(object sender, RoutedEventArgs e) => Change(() => _guide.Move(Step(sender), -1));

    private void OnMoveDown(object sender, RoutedEventArgs e) => Change(() => _guide.Move(Step(sender), 1));

    private void OnRemove(object sender, RoutedEventArgs e) => Change(() => _guide.Remove(Step(sender)));

    private void Change(Action change)
    {
        change();
        Reload();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Se quitarán todas las capturas del grupo. ¿Continuar?", "Zarpazo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _guide.Clear();
        Reload();
    }

    private void OnCopyImages(object sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<string> files = GuideShare.CopyFiles(_guide.Steps, GuideShare.DefaultFolder);
            StatusText.Text = $"{files.Count} {(files.Count == 1 ? "copiada" : "copiadas")}: pega donde quieras";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, $"No se pudieron copiar: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
