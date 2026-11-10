using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Zarpazo.Ocr;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.History;

public partial class HistoryWindow : Window
{
    private const int ThumbnailWidth = 200;

    private readonly CaptureHistory _history;
    private readonly SettingsStore _settings;
    private readonly Action<string> _onEdit;
    private List<Item> _all = [];
    private int _generation; // a reload starts a new reading round; the old one stops

    /// <param name="onEdit">Opens a capture in the editor. The window is hidden by the caller while that lasts.</param>
    internal HistoryWindow(CaptureHistory history, SettingsStore settings, Action<string> onEdit)
    {
        InitializeComponent();
        Theme.TitleBarTheme.Follow(this);
        _history = history;
        _settings = settings;
        _onEdit = onEdit;
        Reload();
        Closed += (_, _) => _generation++;
    }

    /// <param name="select">The path to leave selected, when the list is rebuilt for a change of that very capture.</param>
    internal void Reload(string? select = null)
    {
        var items = new List<Item>();
        foreach (HistoryEntry entry in _history.List())
        {
            try
            {
                items.Add(new Item(entry.Path, CaptureHistory.LoadThumbnail(entry.Path, ThumbnailWidth), $"{entry.Taken:dd/MM/yyyy HH:mm:ss}", entry.CanEdit, entry.MediaPath, entry.Kind, entry.Kept)
                {
                    Text = entry.MediaPath == null ? _history.ReadText(entry.Path) : null,
                });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
            {
                // A damaged or half-written file is skipped instead of breaking the whole list.
            }
        }

        _all = items;
        ApplyFilter();
        if (select != null)
        {
            Items.SelectedItem = (Items.ItemsSource as IEnumerable<Item>)?.FirstOrDefault(item => item.Path == select);
        }
        _ = ReadTextsAsync();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        List<Item> shown = _all.Where(item => CaptureHistory.Matches(item.Text, SearchBox.Text)).ToList();
        Items.ItemsSource = shown;
        EmptyText.Text = _all.Count == 0
            ? "Todavía no hay nada. Aquí aparecen las capturas que copias o guardas, y tus grabaciones."
            : "No hay capturas con ese texto.";
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnSelectionChanged(this, null!);
    }

    /// <summary>Reads the text of the captures that have not been read yet, one at a time, so the search can find them.</summary>
    private async Task ReadTextsAsync()
    {
        int generation = ++_generation;
        if (!TextRecognizer.IsAvailable)
        {
            return;
        }

        List<Item> pending = _all.Where(item => item.MediaPath == null && item.Text == null).ToList();
        for (int i = 0; i < pending.Count && generation == _generation; i++)
        {
            StatusText.Text = $"Leyendo el texto de las capturas… ({i + 1}/{pending.Count})";
            try
            {
                string text = await TextRecognizer.RecognizeAsync(CaptureHistory.Load(pending[i].Path));
                if (generation != _generation)
                {
                    return;
                }

                _history.WriteText(pending[i].Path, text);
                pending[i].Text = text;
                if (SearchBox.Text.Trim().Length > 0)
                {
                    ApplyFilter();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException
                                           or System.Runtime.InteropServices.COMException)
            {
                // This one can't be read (deleted, damaged); the others still can.
            }
        }

        if (generation == _generation)
        {
            StatusText.Text = "";
        }
    }

    private Item? Selected => Items.SelectedItem as Item;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool any = Selected != null;
        CopyButton.IsEnabled = SaveButton.IsEnabled = OpenButton.IsEnabled = DeleteButton.IsEnabled = KeepButton.IsEnabled = any;
        KeepButton.Content = Selected?.Kept == true ? "No conservar" : "Conservar";
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
            FileName = FileNameTemplate.Expand(_settings.Current.FileNameTemplate, DateTime.Now, _settings.Current.AutoSaveCounter + 1),
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

    private void OnKeep(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        Run(() =>
        {
            _history.SetKept(item.Path, !item.Kept);
            Reload(select: item.Path);
            StatusText.Text = item.Kept ? "Ya no se conserva" : "Se conservará hasta que la elimines tú";
        });
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        if (item.Kept && MessageBox.Show(this, "Esta captura está marcada para conservarla. ¿Eliminarla de todos modos?", "Zarpazo",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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

    private sealed record Item(string Path, BitmapSource Thumbnail, string Caption, bool CanEdit, string? MediaPath, string Kind, bool Kept)
    {
        public bool IsRecording => MediaPath != null;

        /// <summary>The text read from the picture; null until it has been read.</summary>
        public string? Text { get; set; }
    }
}
