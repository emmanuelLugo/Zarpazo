using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.History;

public partial class HistoryWindow : Window
{
    private const int ThumbnailWidth = 200;

    private readonly CaptureHistory _history;
    private readonly SettingsStore _settings;

    internal HistoryWindow(CaptureHistory history, SettingsStore settings)
    {
        InitializeComponent();
        _history = history;
        _settings = settings;
        Reload();
    }

    private void Reload()
    {
        var items = new List<Item>();
        foreach (HistoryEntry entry in _history.List())
        {
            try
            {
                items.Add(new Item(entry.Path, CaptureHistory.LoadThumbnail(entry.Path, ThumbnailWidth), $"{entry.Taken:dd/MM/yyyy HH:mm:ss}"));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
            {
                // A damaged or half-written file is skipped instead of breaking the whole list.
            }
        }

        Items.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnSelectionChanged(this, null!);
    }

    private Item? Selected => Items.SelectedItem as Item;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool any = Selected != null;
        CopyButton.IsEnabled = SaveButton.IsEnabled = OpenButton.IsEnabled = DeleteButton.IsEnabled = any;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        Run(() =>
        {
            ImageOutput.CopyToClipboard(CaptureHistory.Load(item.Path), _settings.Current.CopyAsFile);
            StatusText.Text = "Copiada al portapapeles";
        });
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        Run(() =>
        {
            if (ImageOutput.Save(CaptureHistory.Load(item.Path), this, _settings.Current))
            {
                StatusText.Text = "Guardada";
            }
        });
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            Run(() => Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }));
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        Run(() =>
        {
            _history.Delete(item.Path);
            Reload();
            StatusText.Text = "Eliminada";
        });
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_history.Folder);
        Run(() => Process.Start(new ProcessStartInfo(_history.Folder) { UseShellExecute = true }));
    }

    // The clipboard may be locked by another program, a file may be gone: say so instead of crashing.
    private void Run(Action action)
    {
        StatusText.Text = "";
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException
                                       or System.ComponentModel.Win32Exception or NotSupportedException or FileFormatException)
        {
            MessageBox.Show(this, ex.Message, "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed record Item(string Path, BitmapSource Thumbnail, string Caption);
}
