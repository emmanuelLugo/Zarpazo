using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zarpazo.Settings;
using Zarpazo.Theme;
using Image = System.Windows.Controls.Image;

namespace Zarpazo.Guide;

public partial class GuideWindow : Window
{
    private readonly GuideDocument _guide;
    private readonly SettingsStore _settings;
    private Point _pressedAt;
    private GuideStep? _dragStep; // the capture under the pointer when the button went down; null when dragging all of them
    private bool _dragArmed;

    internal GuideWindow(GuideDocument guide, SettingsStore settings)
    {
        InitializeComponent();
        _guide = guide;
        _settings = settings;
        TitleBarTheme.Follow(this);
        guide.Changed += Reload; // the shortcut, the editor and the tray also change the group while this is open
        Closed += (_, _) => guide.Changed -= Reload;
        Reload();
    }

    /// <summary>Shows the captures again; the order, the number of them or the mode changed.</summary>
    internal void Reload()
    {
        Items.ItemsSource = null; // forces the list to draw the new numbers
        Items.ItemsSource = _guide.Steps;
        bool any = _guide.Steps.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = any;
        CopyOneButton.IsEnabled = any;
        DragAll.IsEnabled = any;
        DragAll.Opacity = any ? 1 : 0.5;
        ClearButton.IsEnabled = any;
        ModeBox.IsChecked = _guide.AddsOnCapture;
        HotKeyHint.Text = $"Atajo para copiar el grupo y vaciarlo: {_settings.Current.GroupHotKey.Describe()} (se cambia en Configuración).";
        StatusText.Text = any ? $"{_guide.Steps.Count} {(_guide.Steps.Count == 1 ? "captura" : "capturas")}" : "";
    }

    private static GuideStep Step(object sender) => (GuideStep)((FrameworkElement)sender).DataContext;

    private void OnMoveUp(object sender, RoutedEventArgs e) => _guide.Move(Step(sender), -1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => _guide.Move(Step(sender), 1);

    private void OnRemove(object sender, RoutedEventArgs e) => _guide.Remove(Step(sender));

    private void OnModeClicked(object sender, RoutedEventArgs e) => _guide.AddsOnCapture = ModeBox.IsChecked == true;

    private void OnClear(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Se quitarán todas las capturas del grupo. ¿Continuar?", "Zarpazo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            _guide.Clear();
        }
    }

    private void OnCopyFiles(object sender, RoutedEventArgs e) => Copy(() =>
        GuideShare.CopyFiles(_guide.Steps, GuideShare.DefaultFolder, _settings.Current.GroupNumbering));

    private void OnCopyOne(object sender, RoutedEventArgs e) => Copy(() =>
        GuideShare.CopyAsOneImage(_guide.Steps, _settings.Current));

    private void Copy(Action copy)
    {
        try
        {
            copy();
            StatusText.Text = "Copiado: pega donde quieras";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, $"No se pudo copiar: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- Dragging out ------------------------------------------------------------------------------------------------------
    // Pressing on a thumbnail and moving drags that one capture; the "Arrastrar todas" box drags all of them. Both hand the target
    // program real files, the same as dragging from Explorer.

    private void OnItemsDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        _dragStep = e.OriginalSource is Image { DataContext: GuideStep step } ? step : null; // not from the buttons beside it
        _dragArmed = _dragStep != null;
    }

    private void OnItemsMove(object sender, MouseEventArgs e)
    {
        if (_dragArmed && e.LeftButton == MouseButtonState.Pressed && MovedFarEnough(e))
        {
            _dragArmed = false;
            StartDrag(_dragStep);
        }
    }

    private void OnDragAllDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        _dragStep = null;
        _dragArmed = _guide.Steps.Count > 0;
    }

    private void OnDragAllMove(object sender, MouseEventArgs e)
    {
        if (_dragArmed && e.LeftButton == MouseButtonState.Pressed && MovedFarEnough(e))
        {
            _dragArmed = false;
            StartDrag(null);
        }
    }

    private bool MovedFarEnough(MouseEventArgs e)
    {
        Vector moved = e.GetPosition(this) - _pressedAt;
        return Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void StartDrag(GuideStep? only)
    {
        try
        {
            List<string> files = GuideShare.WriteFiles(_guide.Steps, GuideShare.DefaultFolder, _settings.Current.GroupNumbering);
            string[] dragged = only == null ? [.. files] : [files[only.Number - 1]];
            DragDrop.DoDragDrop(this, new DataObject(DataFormats.FileDrop, dragged), DragDropEffects.Copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"No se pudo arrastrar: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
