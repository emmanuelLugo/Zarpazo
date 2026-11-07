using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Zarpazo.Settings;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>One recording of an area, from the first picture to the finished GIF being copied or saved.</summary>
internal sealed class RecordingSession
{
    private const int MaxSeconds = 30;
    private const long MaxBytes = 400L * 1024 * 1024;
    private static readonly TimeSpan TempFileLifetime = TimeSpan.FromDays(7);

    private readonly Rectangle _region;
    private readonly SettingsStore _settings;
    private readonly Action _closed;
    private readonly string _folder;
    private RecordingOutline? _outline;
    private RecordingBar? _bar;
    private ScreenRecorder? _recorder;
    private DispatcherTimer? _clock;
    private string? _gifPath;
    private bool _closedAlready;

    /// <param name="region">The area to record, in physical screen pixels.</param>
    /// <param name="closed">Called when the person is done with it, so the owner can let go of it.</param>
    /// <param name="folder">Where the GIF is written first; the tests use their own.</param>
    public RecordingSession(Rectangle region, SettingsStore settings, Action closed, string? folder = null)
    {
        _region = region;
        _settings = settings;
        _closed = closed;
        _folder = folder ?? Path.Combine(Path.GetTempPath(), "Zarpazo");
    }

    public bool IsRecording { get; private set; }

    public void Start()
    {
        _outline = new RecordingOutline(_region);
        _bar = new RecordingBar();
        _bar.StopRequested += (_, _) => Stop();
        _bar.CancelRequested += (_, _) => Close();
        _bar.CopyRequested += (_, _) => Copy();
        _bar.SaveRequested += (_, _) => Save();
        _bar.CloseRequested += (_, _) => Close();

        _recorder = new ScreenRecorder(_region, TimeSpan.FromSeconds(MaxSeconds), MaxBytes);
        // Raised on the capture thread.
        _recorder.LimitReached += reason => _bar.Dispatcher.BeginInvoke(() => Finish(reason));

        _bar.ShowRecording(TimeSpan.Zero, TimeSpan.FromSeconds(MaxSeconds));
        _bar.PlaceNear(_region);
        _outline.Show();
        _bar.Show();

        IsRecording = true;
        _recorder.Start();
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => _bar.ShowRecording(_recorder.Elapsed, TimeSpan.FromSeconds(MaxSeconds));
        _clock.Start();
    }

    /// <summary>Ends the recording and makes the GIF.</summary>
    public void Stop() => Finish(RecordingEnd.Stopped);

    private async void Finish(RecordingEnd reason)
    {
        if (!IsRecording || _recorder == null || _bar == null)
        {
            return; // already stopped, e.g. the limit and the button at the same moment
        }
        IsRecording = false;
        _clock?.Stop();
        (IReadOnlyList<RecordedFrame> frames, int durationMs) = _recorder.Stop();
        _outline?.Close();
        if (frames.Count == 0)
        {
            _bar.ShowFailure("La grabación fue demasiado corta."); // stopped before the first picture was taken
            return;
        }
        _bar.ShowEncoding();

        try
        {
            string path = await Task.Run(() => WriteGif(frames, durationMs));
            if (_closedAlready)
            {
                return;
            }
            _gifPath = path;
            _bar.ShowDone($"{Describe(reason)}GIF listo · {SizeText(new FileInfo(path).Length)}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // This runs from an async void method: an exception left uncaught here would take the whole app down.
            if (!_closedAlready)
            {
                _bar.ShowFailure($"No se pudo crear el GIF: {ex.Message}");
            }
        }
    }

    private string WriteGif(IReadOnlyList<RecordedFrame> frames, int durationMs)
    {
        Directory.CreateDirectory(_folder);
        DeleteOldGifs(_folder);

        // No spaces, so a pasted path needs no quotes.
        string path = Path.Combine(_folder, $"Zarpazo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.gif");
        using FileStream file = File.Create(path);
        GifEncoder.Encode(frames, _recorder!.Width, _recorder.Height, durationMs, file);
        return path;
    }

    private static void DeleteOldGifs(string folder)
    {
        foreach (string old in Directory.EnumerateFiles(folder, "*.gif"))
        {
            try
            {
                if (DateTime.Now - File.GetLastWriteTime(old) > TempFileLifetime)
                {
                    File.Delete(old);
                }
            }
            catch (IOException)
            {
                // Still open somewhere; it goes next time.
            }
        }
    }

    private static string Describe(RecordingEnd reason) => reason switch
    {
        RecordingEnd.TimeLimit => $"Límite de {MaxSeconds} s · ",
        RecordingEnd.MemoryLimit => "Límite de memoria · ",
        _ => "",
    };

    private static string SizeText(long bytes) =>
        bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / (1024.0 * 1024.0):0.0} MB";

    // Pasted as a file, which is what chats and documents accept for an animated image.
    private void Copy()
    {
        if (_gifPath == null)
        {
            return;
        }

        try
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { _gifPath });
            Clipboard.SetDataObject(data, copy: true);
            _bar?.ShowDone($"GIF copiado · {SizeText(new FileInfo(_gifPath).Length)}");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _bar?.ShowFailure("No se pudo copiar: otro programa usa el portapapeles.");
        }
    }

    private void Save()
    {
        if (_gifPath == null || _bar == null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar GIF",
            FileName = $"Zarpazo {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
            Filter = "Imagen GIF (*.gif)|*.gif",
            AddExtension = true,
            DefaultExt = ".gif",
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
            File.Copy(_gifPath, dialog.FileName, overwrite: true);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _bar.ShowFailure($"No se pudo guardar: {ex.Message}");
        }
    }

    /// <summary>Drops a recording in progress, or closes the window once the GIF has been dealt with.</summary>
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
            _recorder?.Stop();
        }
        _outline?.Close();
        _bar?.Close();
        _closed();
    }
}
