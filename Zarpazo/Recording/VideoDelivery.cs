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
    private string? _historyPath; // only touched by the history work below, one piece at a time
    private bool _autoSaved;
    private Task _historyWork = Task.CompletedTask;
    private static int _pendingWrites;

    /// <summary>True while any delivery is still writing to the history (a recording can be hundreds of megabytes): the app is not idle yet.</summary>
    public static bool HasPendingWrites => Volatile.Read(ref _pendingWrites) > 0;

    /// <summary>Raised, from a background thread, when the last of the writes to the history has finished.</summary>
    public static event Action? WritesFinished;

    /// <summary>What is being written to the history; finished when there is nothing left to write.</summary>
    public Task HistoryWork => _historyWork;

    /// <param name="original">The clean recording the editor works on.</param>
    /// <param name="readyPath">Where the finished video is written; its name is the one people see when they paste it.</param>
    /// <param name="thumbnail">The picture for the history; without it nothing goes there.</param>
    /// <param name="historyPath">The history entry being edited again: what is handed out replaces it, and closing without
    /// handing out anything leaves its video alone and only updates its edit.</param>
    public VideoDelivery(string original, string readyPath, SettingsStore? settings = null, CaptureHistory? history = null, BitmapSource? thumbnail = null,
        string? historyPath = null)
    {
        Original = original;
        ReadyPath = readyPath;
        _settings = settings;
        _history = history;
        _thumbnail = thumbnail;
        if (_thumbnail is { IsFrozen: false, CanFreeze: true })
        {
            _thumbnail.Freeze(); // the history is written from a background thread, which may only read a frozen picture
        }
        _historyPath = historyPath;
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
    /// <param name="project">The edit the video is made from, kept in the history with the clean recording to edit it again.</param>
    /// <exception cref="Exception">Whatever <paramref name="make"/> throws; the old file is kept in that case.</exception>
    public async Task<string> PrepareAsync(string recipe, Func<string, Task> make, VideoProject? project = null)
    {
        if (IsCurrent(recipe))
        {
            return ReadyPath;
        }

        await _historyWork; // the video of before may still be on its way to the history, from this very file

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
        // Copying the video and the clean recording into the history is slow for a long recording, and nobody waits for it: the
        // video is ready to paste. It goes on in the background, one piece after another.
        string ready = ReadyPath;
        QueueHistoryWork(() => KeepInHistory(ready, project));
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

    // Runs the work after the earlier work has finished, off the window's thread. The app counts it as busy until the last one is done.
    private void QueueHistoryWork(Action work)
    {
        Interlocked.Increment(ref _pendingWrites);
        _historyWork = _historyWork.ContinueWith(_ =>
        {
            try
            {
                work();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The history is a convenience on top of a video that is already made: whatever went wrong, it must not
                // fail the next copy or save, which waits for this work.
            }
            finally
            {
                if (Interlocked.Decrement(ref _pendingWrites) == 0)
                {
                    WritesFinished?.Invoke();
                }
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// <see cref="Finish"/> in the background, after the writes already under way: closing the editor does not wait for the copy.
    /// Returns what is being written, which is done when the history has everything.
    /// </summary>
    public Task FinishAsync(VideoProject? project = null)
    {
        QueueHistoryWork(() => Finish(project));
        return _historyWork;
    }

    /// <summary>
    /// Called when the editor closes with the edit as it was left. A recording never handed out goes to the history as it was
    /// recorded, with that edit; one already in the history (handed out before, or opened from it) keeps its video and gets the
    /// latest edit, so what was done after the last copy or save is not lost.
    /// </summary>
    public void Finish(VideoProject? project = null)
    {
        if (_historyPath != null)
        {
            TryUpdateProject(_historyPath, project);
        }
        else if (!Delivered)
        {
            KeepInHistory(Original, project);
        }
    }

    private void TryUpdateProject(string entry, VideoProject? project)
    {
        if (_history == null || project == null)
        {
            return;
        }

        try
        {
            _history.UpdateProject(entry, project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The recording is already made, so a full disk or a locked folder must not turn that into an error.
    private void KeepInHistory(string path, VideoProject? project = null)
    {
        if (_history == null || _thumbnail == null || _settings?.Current.KeepHistory != true)
        {
            return;
        }

        try
        {
            bool kept = _historyPath != null && _history.IsKept(_historyPath);
            if (_historyPath != null)
            {
                _history.Delete(_historyPath); // replaced by the newer version; the editor works on its own copy of the recording
            }
            // The clean recording goes along (with the edit) so the video can be edited again from the history.
            _historyPath = _history.AddRecording(path, _thumbnail, original: Original, project: project);
            if (kept)
            {
                _history.SetKept(_historyPath, true); // what the person marked to keep stays so
            }
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
