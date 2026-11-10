using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Zarpazo.Theme;

namespace Zarpazo.Guide;

public partial class GuideWindow : Window
{
    private readonly GuideDocument _guide;

    internal GuideWindow(GuideDocument guide)
    {
        InitializeComponent();
        _guide = guide;
        TitleBox.Text = guide.Title;
        TitleBarTheme.Follow(this);
        Reload();
    }

    /// <summary>Shows the steps again; the order or the number of them changed.</summary>
    internal void Reload()
    {
        Items.ItemsSource = null; // forces the list to draw the new numbers
        Items.ItemsSource = _guide.Steps;
        bool any = _guide.Steps.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        PdfButton.IsEnabled = any;
        HtmlButton.IsEnabled = any;
        ClearButton.IsEnabled = any || _guide.Title.Length > 0;
        StatusText.Text = any ? $"{_guide.Steps.Count} {(_guide.Steps.Count == 1 ? "paso" : "pasos")}" : "";
    }

    private void OnTitleChanged(object sender, TextChangedEventArgs e) => _guide.Title = TitleBox.Text;

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
        var answer = MessageBox.Show(this, "Se borrarán todos los pasos de la guía. ¿Continuar?", "Zarpazo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _guide.Clear();
        TitleBox.Text = "";
        Reload();
    }

    private void OnSaveProject(object sender, RoutedEventArgs e) =>
        Export($"Guía de Zarpazo (*{GuideProject.Extension})|*{GuideProject.Extension}", GuideProject.Extension,
            path => GuideProject.Save(_guide, path), "Guardar guía", reveal: false);

    private void OnOpenProject(object sender, RoutedEventArgs e)
    {
        if (_guide.Steps.Count > 0 && MessageBox.Show(this, "La guía actual se reemplazará por la que abras. ¿Continuar?", "Zarpazo",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Abrir guía",
            Filter = $"Guía de Zarpazo (*{GuideProject.Extension})|*{GuideProject.Extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            GuideProject.Load(_guide, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) // InvalidDataException is an IOException
        {
            MessageBox.Show(this, $"No se pudo abrir la guía: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TitleBox.Text = _guide.Title;
        Reload();
        StatusText.Text = $"Abierta: {Path.GetFileName(dialog.FileName)}";
    }

    private void OnExportPdf(object sender, RoutedEventArgs e) =>
        Export("Guía en PDF (*.pdf)|*.pdf", ".pdf", path =>
        {
            using FileStream file = File.Create(path);
            PdfWriter.Write(file, GuidePages.Render(_guide.Title, _guide.Steps));
        });

    private void OnExportHtml(object sender, RoutedEventArgs e) =>
        Export("Página web (*.html)|*.html", ".html", path => File.WriteAllText(path, GuideHtml.Build(_guide.Title, _guide.Steps)));

    private void Export(string filter, string extension, Action<string> write, string title = "Exportar guía", bool reveal = true)
    {
        string name = string.Concat((_guide.Title.Length > 0 ? _guide.Title : "Guía").Split(Path.GetInvalidFileNameChars()));
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = name,
            Filter = filter,
            AddExtension = true,
            DefaultExt = extension,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            write(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"No se pudo guardar: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            Cursor = null;
        }

        StatusText.Text = $"Guardada: {dialog.FileName}";
        if (reveal)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"") { UseShellExecute = true });
        }
    }
}
