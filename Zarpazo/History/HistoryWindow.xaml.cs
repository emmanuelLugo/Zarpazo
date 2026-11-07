using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.History;

public partial class HistoryWindow : Window
{
    private const int ThumbnailWidth = 200;

    private readonly CaptureHistory _history;
    private readonly SettingsStore _settings;
    private readonly Action<string> _onEdit;

    /// <param name="onEdit">Opens a capture in the editor. The window is hidden by the caller while that lasts.</param>
    internal HistoryWindow(CaptureHistory history, SettingsStore settings, Action<string> onEdit)
    {
        InitializeComponent();
        _history = history;
        _settings = settings;
        _onEdit = onEdit;
        Reload();
    }

    internal void Reload()
    {
        var items = new List<Item>();
        foreach (HistoryEntry entry in _history.List())
        {
            try
            {
                items.Add(new Item(entry.Path, CaptureHistory.LoadThumbnail(entry.Path, ThumbnailWidth), $"{entry.Taken:dd/MM/yyyy HH:mm:ss}", entry.CanEdit, entry.MediaPath, entry.Kind));
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
        EditButton.IsEnabled = Selected?.CanEdit == true; // captures from before the history could reopen them have no saved state
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Selected is { CanEdit: true } item)
        {
            _onEdit(item.Path);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        Run(() =>
        {
            if (item.MediaPath is { } media)
            {
                // A recording goes on the clipboard as a file, which is what chats and documents take for it.
                var data = new DataObject();
                data.SetFileDropList(new System.Collections.Specialized.StringCollection { media });
                Clipboard.SetDataObject(data, copy: true);
                StatusText.Text = $"{item.Kind} copiado al portapapeles";
                return;
            }
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
            if (item.MediaPath is { } media)
            {
                SaveRecording(media, item.Kind);
            }
            else if (ImageOutput.Save(CaptureHistory.Load(item.Path), this, _settings.Current))
            {
                StatusText.Text = "Guardada";
            }
        });
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            Run(() => Process.Start(new ProcessStartInfo(item.MediaPath ?? item.Path) { UseShellExecute = true }));
        }
    }

    private void SaveRecording(string media, string kind)
    {
        string extension = Path.GetExtension(media);
        var dialog = new SaveFileDialog
        {
            Title = $"Guardar {kind.ToLowerInvariant()}",
            FileName = $"Zarpazo {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
            Filter = extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ? "Imagen GIF (*.gif)|*.gif" : "Video MP4 (*.mp4)|*.mp4",
            AddExtension = true,
            DefaultExt = extension,
            InitialDirectory = Directory.Exists(_settings.Current.SaveFolder)
                ? _settings.Current.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) == true)
        {
            File.Copy(media, dialog.FileName, overwrite: true);
            StatusText.Text = "Guardado";
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

    private sealed record Item(string Path, BitmapSource Thumbnail, string Caption, bool CanEdit, string? MediaPath, string Kind)
    {
        public bool IsRecording => MediaPath != null;
    }
}
