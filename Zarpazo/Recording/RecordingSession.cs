using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Zarpazo.Settings;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>One recording of an area, from the first picture to the finished file being copied or saved.</summary>
internal sealed class RecordingSession
{
    private readonly Rectangle _region;
    private readonly SettingsStore _settings;
    private readonly Action _closed;
    private readonly string _folder;
    private readonly RecordingJob _job;
    private RecordingOutline? _outline;
    private RecordingBar? _bar;
    private DispatcherTimer? _clock;
    private string? _filePath;
    private bool _closedAlready;

    /// <param name="region">The area to record, in physical screen pixels.</param>
    /// <param name="closed">Called when the person is done with it, so the owner can let go of it.</param>
    /// <param name="folder">Where the file is written first; the tests use their own.</param>
    public RecordingSession(Rectangle region, RecordingOptions options, SettingsStore settings, Action closed, string? folder = null)
    {
        _region = region;
        _settings = settings;
        _closed = closed;
        _folder = folder ?? Path.Combine(Path.GetTempPath(), "Zarpazo");
        _job = options.Format == RecordingFormat.Video ? new VideoJob(region, options) : new GifJob(region);
    }

    public bool IsRecording { get; private set; }

    /// <exception cref="InvalidOperationException">Something the recording needs is missing, such as a microphone.</exception>
    public void Start()
    {
        _outline = new RecordingOutline(_region);
        _bar = new RecordingBar();
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

        try
        {
            _job.Start(_folder);
        }
        catch
        {
            Close(); // nothing was recorded: the frame and the bar must not stay on screen
            throw;
        }

        IsRecording = true;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => _bar.ShowRecording(_job.Elapsed, _job.MaxDuration, _job.Meters);
        _clock.Start();
    }

    /// <summary>Ends the recording and makes the file.</summary>
    public void Stop() => Finish(RecordingEnd.Stopped);

    private async void Finish(RecordingEnd reason)
    {
        if (!IsRecording || _bar == null)
        {
            return; // already stopped, e.g. the limit and the button at the same moment
        }
        IsRecording = false;
        _clock?.Stop();
        _outline?.Close();
        _bar.ShowEncoding($"Creando el {_job.Label}…");

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
            _filePath = path;
            _bar.ShowDone($"{Describe(reason)}{Capitalized(_job.Label)} listo · {SizeText(new FileInfo(path).Length)}");
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
            FileName = $"Zarpazo {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
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
        if (IsRecording)
        {
            IsRecording = false;
            _clock?.Stop();
            _job.Abort();
        }
        _outline?.Close();
        _bar?.Close();
        _closed();
    }
}
