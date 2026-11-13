using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Zarpazo.History;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.Recording;

/// <summary>
/// The finished video the editor hands out, like Recordly's export: made from the clean recording with the cuts, zooms,
/// frame and pointer chosen, into one file that copying and saving share. It is made again only when something changed
/// since. The first one made goes to the history and, with automatic saving on, to the folder; a later one replaces it in
/// the history. A recording closed without ever being handed out still goes to the history, as it was recorded.
/// </summary>
internal sealed class VideoDelivery
{
    private readonly SettingsStore? _settings;
    private readonly CaptureHistory? _history;
    private readonly BitmapSource? _thumbnail;
    private string? _madeFor; // what the file at ReadyPath was made with; null while there is none
    private string? _historyPath;
    private bool _autoSaved;

    /// <param name="original">The clean recording the editor works on.</param>
    /// <param name="readyPath">Where the finished video is written; its name is the one people see when they paste it.</param>
    /// <param name="thumbnail">The picture for the history; without it nothing goes there.</param>
    public VideoDelivery(string original, string readyPath, SettingsStore? settings = null, CaptureHistory? history = null, BitmapSource? thumbnail = null)
    {
        Original = original;
        ReadyPath = readyPath;
        _settings = settings;
        _history = history;
        _thumbnail = thumbnail;
    }

    public string Original { get; }

    public string ReadyPath { get; }

    /// <summary>Whether the video was ever copied or saved.</summary>
    public bool Delivered => _madeFor != null;

    /// <summary>Whether the finished file was made with exactly <paramref name="recipe"/>.</summary>
    public bool IsCurrent(string recipe) => _madeFor == recipe && File.Exists(ReadyPath);

    /// <summary>
    /// The finished video for <paramref name="recipe"/> (a description of every choice that changes it), made with
    /// <paramref name="make"/> unless the one at hand was made with the same.
    /// </summary>
    /// <param name="make">Writes the video into the path it is given.</param>
    /// <exception cref="Exception">Whatever <paramref name="make"/> throws; the old file is kept in that case.</exception>
    public async Task<string> PrepareAsync(string recipe, Func<string, Task> make)
    {
        if (IsCurrent(recipe))
        {
            return ReadyPath;
        }

        // Made next to it first: if it fails half-way, the finished video of before is still whole.
        string making = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ReadyPath))!, Path.GetFileNameWithoutExtension(ReadyPath) + ".haciendo" + Path.GetExtension(ReadyPath));
        try
        {
            await make(making);
            File.Move(making, ReadyPath, overwrite: true);
        }
        catch
        {
            TryDelete(making);
            throw;
        }

        _madeFor = recipe;
        KeepInHistory(ReadyPath);
        if (!_autoSaved && _settings != null && new AutoSaver(_settings).IsEnabled)
        {
            _autoSaved = true; // once: a second version would only fill the folder
            try
            {
                new AutoSaver(_settings).SaveFile(ReadyPath);
                _settings.Save(); // the counter it took
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return ReadyPath;
    }

    /// <summary>Puts the file on the clipboard, to paste in a chat or a document; false if another program holds the clipboard.</summary>
    public static bool CopyToClipboard(string path)
    {
        try
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { path });
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>Called when the editor closes: a recording never handed out goes to the history as it was recorded.</summary>
    public void Finish()
    {
        if (!Delivered)
        {
            KeepInHistory(Original);
        }
    }

    // The recording is already made, so a full disk or a locked folder must not turn that into an error.
    private void KeepInHistory(string path)
    {
        if (_history == null || _thumbnail == null || _settings?.Current.KeepHistory != true)
        {
            return;
        }

        try
        {
            if (_historyPath != null)
            {
                _history.Delete(_historyPath); // replaced by the newer version
            }
            _historyPath = _history.AddRecording(path, _thumbnail);
            _history.DeleteOlderThan(_settings.Current.HistoryMaxAgeDays, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _historyPath = null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
