using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Zarpazo.History;
using Zarpazo.Output;
using Zarpazo.Settings;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>One recording of an area, from the countdown to the finished file being copied or saved.</summary>
internal sealed class RecordingSession
{
    private readonly Rectangle _region;
    private readonly SettingsStore _settings;
    private readonly CaptureHistory _history;
    private readonly Action _closed;
    private readonly string _folder;
    private readonly RecordingJob _job;
    private RecordingOutline? _outline;
    private RecordingCountdown? _countdown;
    private RecordingBar? _bar;
    private DispatcherTimer? _clock;
    private DispatcherTimer? _countdownTimer;
    private string? _filePath;
    private string? _historyPath; // this recording's entry in the history, replaced if it is trimmed
    private bool _closedAlready;
    private bool _finishing; // the file is being made or is made: closing no longer throws it away

    /// <param name="region">The area to record, in physical screen pixels.</param>
    /// <param name="closed">Called when the person is done with it, so the owner can let go of it.</param>
    /// <param name="folder">Where the file is written first; the tests use their own.</param>
    public RecordingSession(Rectangle region, RecordingOptions options, SettingsStore settings, CaptureHistory history, Action closed, string? folder = null)
    {
        _region = region;
        _settings = settings;
        _history = history;
        _closed = closed;
        _folder = folder ?? Path.Combine(Path.GetTempPath(), "Zarpazo");
        _job = options.Format == RecordingFormat.Video
            ? new VideoJob(region, options, settings.Current.RecordAudioOffsetMs, settings.Current.RecordingQuality)
            : new GifJob(region, RecordingEffects.FromOptions(options), settings.Current.RecordingQuality);
        _job.NameTemplate = settings.Current.FileNameTemplate;
        _job.NameCounter = settings.Current.AutoSaveCounter + 1;
    }

    public bool IsCountingDown { get; private set; }

    public bool IsRecording { get; private set; }

    public bool IsPaused => IsRecording && _job.IsPaused;

    /// <summary>Raised when it starts or stops counting down, recording or pausing, so the tray icon can follow.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Raised when a finished video opens in the editor, which takes it from there (copy, save, close) and outlives the
    /// session: the session closes as the editor opens.
    /// </summary>
    public event Action<VideoEditWindow>? EditorOpened;

    /// <summary>Raised when the recording goes ahead without something asked for, such as a sound source; the message says what.</summary>
    public event Action<string>? Warned;

    /// <summary>Raised when the recording could not start after a countdown; the message says why.</summary>
    public event Action<string>? Failed;

    /// <param name="countdownSeconds">Seconds to count down first; 0 starts at once.</param>
    /// <exception cref="InvalidOperationException">Something the recording needs is missing, such as a microphone.</exception>
    public void Start(int countdownSeconds)
    {
        // Before anything is shown: a missing microphone is reported at once, not after the countdown.
        _job.Prepare(_folder);
        if (_job.Warning is { } warning)
        {
            Warned?.Invoke(warning);
        }

        _outline = new RecordingOutline(_region);
        _bar = new RecordingBar();
        _bar.PauseRequested += (_, _) => TogglePause();
        _bar.GainChanged += (source, gain) => _job.SetSourceGain(source, gain);
        _bar.StopRequested += (_, _) => Stop();
        _bar.CancelRequested += (_, _) => Close();
        _bar.CopyRequested += (_, _) => Copy();
        _bar.SaveRequested += (_, _) => Save();
        _bar.CloseRequested += (_, _) => Close();
        // Raised on a capture thread.
        _job.LimitReached += reason => _bar.Dispatcher.BeginInvoke(() => Finish(reason));

        _bar.ShowRecording(TimeSpan.Zero, _job.MaxDuration, _job.Meters);
        _bar.PlaceNear(_region);
        _outline.Show();
        _bar.Show();

        if (countdownSeconds > 0)
        {
            BeginCountdown(countdownSeconds);
        }
        else
        {
            try
            {
                BeginRecording();
            }
            catch
            {
                Close(); // nothing was recorded: the frame and the bar must not stay on screen
                throw;
            }
        }
    }

    private void BeginCountdown(int seconds)
    {
        IsCountingDown = true;
        _countdown = new RecordingCountdown(_region);
        _countdown.ShowNumber(seconds);
        _countdown.Show();
        _bar!.ShowCountdown(seconds);
        StateChanged?.Invoke();

        int left = seconds;
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            left--;
            if (left > 0)
            {
                _countdown?.ShowNumber(left);
                _bar?.ShowCountdown(left);
                return;
            }

            _countdownTimer?.Stop();
            _countdown?.Close();
            _countdown = null;
            IsCountingDown = false;
            try
            {
                BeginRecording();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Close();
                Failed?.Invoke($"No se pudo empezar a grabar: {ex.Message}");
            }
        };
        _countdownTimer.Start();
    }

    private void BeginRecording()
    {
        _job.Begin();
        IsRecording = true;
        _bar!.ShowRecording(TimeSpan.Zero, _job.MaxDuration, _job.Meters);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => _bar!.ShowRecording(_job.Elapsed, _job.MaxDuration, _job.Meters, _job.IsPaused);
        _clock.Start();
        StateChanged?.Invoke();
    }

    /// <summary>Pauses a recording that is running, or carries on with one that is paused.</summary>
    public void TogglePause()
    {
        if (!IsRecording || _bar == null)
        {
            return;
        }

        if (_job.IsPaused)
        {
            _job.Resume();
        }
        else
        {
            _job.Pause();
        }
        _bar.ShowRecording(_job.Elapsed, _job.MaxDuration, _job.Meters, _job.IsPaused);
        StateChanged?.Invoke();
    }

    /// <summary>Ends the recording and makes the file; during the countdown it cancels instead.</summary>
    public void Stop()
    {
        if (IsCountingDown)
        {
            Close();
            return;
        }
        Finish(RecordingEnd.Stopped);
    }

    private async void Finish(RecordingEnd reason)
    {
        if (!IsRecording || _bar == null)
        {
            return; // already stopped, e.g. the limit and the button at the same moment
        }
        IsRecording = false;
        _finishing = true;
        _clock?.Stop();
        _outline?.Close();
        _bar.ShowEncoding($"Creando el {_job.Label}…");
        StateChanged?.Invoke();

        try
        {
            string? path = await _job.FinishAsync();
            if (_closedAlready)
            {
                return;
            }
            if (path == null)
            {
                _bar.ShowFailure("La grabación fue demasiado corta.");
                return;
            }
            if (_job.CanEdit)
            {
                OpenEditor(path, reason);
                return;
            }
            _filePath = path;
            _historyPath = AddToHistory(path);
            _job.ReleaseThumbnail();
            MemoryTrim.Release();
            string saved = AutoSave(path);
            _bar.ShowDone($"{Describe(reason)}{Capitalized(_job.Label)} listo · {SizeText(new FileInfo(path).Length)}{saved}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // This runs from an async void method: an exception left uncaught here would take the whole app down.
            if (!_closedAlready)
            {
                _bar.ShowFailure($"No se pudo crear el {_job.Label}: {ex.Message}");
            }
        }
    }

    // Like Recordly, a finished video opens in the editor, which copies and saves it: the bar goes. The clean recording
    // becomes "name.original.mp4" for the editor to work on, and the finished video takes the name people see when they
    // paste it. Should the files not move, the editor works on the recording where it is.
    private void OpenEditor(string path, RecordingEnd reason)
    {
        string original = path;
        try
        {
            original = RecordingFiles.OriginalFor(path);
            File.Move(path, original, overwrite: true);
            if (File.Exists(CursorTrack.PathFor(path)))
            {
                File.Move(CursorTrack.PathFor(path), CursorTrack.PathFor(original), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            original = File.Exists(path) ? path : original;
        }

        string ready = original == path ? Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_listo.mp4") : path;
        var delivery = new VideoDelivery(original, ready, _settings, _history, _job.Thumbnail);
        string notice = reason switch
        {
            RecordingEnd.TimeLimit => $"La grabación se detuvo al llegar al límite de {LimitText(_job.MaxDuration)}.",
            RecordingEnd.MemoryLimit => "La grabación se detuvo por el límite de memoria.",
            _ => "",
        };
        var editor = new VideoEditWindow(original, _settings, delivery, notice);
        _job.ReleaseThumbnail();
        editor.Show();
        editor.Activate();
        EditorOpened?.Invoke(editor);
        Close();
    }

    // The recording is already made, so a full disk or a locked folder must not turn that into an error.
    private string? AddToHistory(string path)
    {
        if (!_settings.Current.KeepHistory || _job.Thumbnail == null)
        {
            return null;
        }

        try
        {
            string added = _history.AddRecording(path, _job.Thumbnail);
            _history.DeleteOlderThan(_settings.Current.HistoryMaxAgeDays, DateTime.Now);
            return added;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // With automatic saving on the finished file also goes to the folder; what is added to the bar's message says so.
    private string AutoSave(string path)
    {
        var saver = new AutoSaver(_settings);
        if (!saver.IsEnabled)
        {
            return "";
        }

        try
        {
            saver.SaveFile(path);
            return " · guardado";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return " · no se pudo guardar solo";
        }
    }

    private string Describe(RecordingEnd reason) => reason switch
    {
        RecordingEnd.TimeLimit => $"Límite de {LimitText(_job.MaxDuration)} · ",
        RecordingEnd.MemoryLimit => "Límite de memoria · ",
        _ => "",
    };

    private static string LimitText(TimeSpan limit) => limit.TotalMinutes >= 1 ? $"{(int)limit.TotalMinutes} min" : $"{(int)limit.TotalSeconds} s";

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string SizeText(long bytes) =>
        bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / (1024.0 * 1024.0):0.0} MB";

    // Pasted as a file, which is what chats and documents accept for an animated image or a video.
    private void Copy()
    {
        if (_filePath == null)
        {
            return;
        }

        try
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { _filePath });
            Clipboard.SetDataObject(data, copy: true);
            _bar?.ShowDone($"{Capitalized(_job.Label)} copiado · {SizeText(new FileInfo(_filePath).Length)}");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _bar?.ShowFailure("No se pudo copiar: otro programa usa el portapapeles.");
        }
    }

    private void Save()
    {
        if (_filePath == null || _bar == null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"Guardar {_job.Label}",
            FileName = FileNameTemplate.Expand(_settings.Current.FileNameTemplate, DateTime.Now, _settings.Current.AutoSaveCounter + 1),
            Filter = _job.FileFilter,
            AddExtension = true,
            DefaultExt = _job.Extension,
            InitialDirectory = Directory.Exists(_settings.Current.SaveFolder)
                ? _settings.Current.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(_bar) != true)
        {
            return;
        }

        try
        {
            File.Copy(_filePath, dialog.FileName, overwrite: true);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _bar.ShowFailure($"No se pudo guardar: {ex.Message}");
        }
    }

    /// <summary>Drops a recording in progress, or closes the window once the file has been dealt with.</summary>
    public void Close()
    {
        if (_closedAlready)
        {
            return;
        }
        _closedAlready = true;
        _countdownTimer?.Stop();
        _countdown?.Close();
        if (IsRecording || IsCountingDown || !_finishing)
        {
            // Also a recording that failed to start: its empty file goes.
            IsRecording = false;
            IsCountingDown = false;
            _clock?.Stop();
            _job.Abort();
        }
        _outline?.Close();
        _bar?.Close();
        _closed();
        MemoryTrim.Release();
        StateChanged?.Invoke();
    }
}
