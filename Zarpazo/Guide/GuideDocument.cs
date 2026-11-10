using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.Guide;

/// <summary>
/// The group of captures being put together, in order. The pictures live in a folder, one PNG each, with a small file that
/// remembers the order, so the group survives closing Zarpazo and a long one does not sit in memory.
/// </summary>
internal sealed class GuideDocument
{
    private const string OrderFile = "group.json";

    private readonly string _folder;
    private readonly List<GuideStep> _steps = [];
    private bool _addsOnCapture;

    public GuideDocument(string folder)
    {
        _folder = folder;
        Load();
    }

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zarpazo", "Guide");

    public IReadOnlyList<GuideStep> Steps => _steps;

    /// <summary>
    /// While on, a capture finished with Enter is added to the group instead of copied. Only for this run: it is off the
    /// next time Zarpazo starts, so nobody finds their captures going somewhere unexpected.
    /// </summary>
    public bool AddsOnCapture
    {
        get => _addsOnCapture;
        set
        {
            if (_addsOnCapture != value)
            {
                _addsOnCapture = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>The captures or the mode changed.</summary>
    public event Action? Changed;

    /// <summary>Adds the picture as the last one.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public GuideStep Add(BitmapSource image)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, $"{Guid.NewGuid():N}.png");
        ImageOutput.Write(path, image);

        var step = new GuideStep(path);
        _steps.Add(step);
        Renumber();
        Saved();
        return step;
    }

    public void Remove(GuideStep step)
    {
        if (_steps.Remove(step))
        {
            Delete(step.ImagePath);
            Renumber();
            Saved();
        }
    }

    /// <summary>Moves a capture up (negative) or down (positive) by that many places, stopping at the ends.</summary>
    public void Move(GuideStep step, int offset)
    {
        int from = _steps.IndexOf(step);
        if (from < 0)
        {
            return;
        }

        int to = Math.Clamp(from + offset, 0, _steps.Count - 1);
        _steps.RemoveAt(from);
        _steps.Insert(to, step);
        Renumber();
        Saved();
    }

    /// <summary>Starts over: nothing in the group, and the pictures deleted.</summary>
    public void Clear()
    {
        _steps.Clear();
        if (Directory.Exists(_folder))
        {
            foreach (string file in Directory.EnumerateFiles(_folder, "*.png"))
            {
                Delete(file);
            }
        }
        Saved();
    }

    // What the last run left: the captures listed in the order file, if they are still there. A damaged file means an empty
    // group, and nothing is deleted then, in case the pictures are worth rescuing.
    private void Load()
    {
        string order = Path.Combine(_folder, OrderFile);
        if (!File.Exists(order))
        {
            return;
        }

        try
        {
            string[] names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(order)) ?? [];
            foreach (string name in names)
            {
                string path = Path.Combine(_folder, Path.GetFileName(name)); // a name only: never a path out of the folder
                if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    _steps.Add(new GuideStep(path));
                }
            }

            var kept = _steps.Select(step => step.ImagePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string stray in Directory.EnumerateFiles(_folder, "*.png").Where(file => !kept.Contains(file)))
            {
                Delete(stray);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _steps.Clear();
        }
        Renumber();
    }

    // The order is written after every change. A failure is not worth interrupting anyone: the group just won't survive a restart.
    private void Saved()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, OrderFile), JsonSerializer.Serialize(_steps.Select(step => Path.GetFileName(step.ImagePath))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        Changed?.Invoke();
    }

    private void Renumber()
    {
        for (int i = 0; i < _steps.Count; i++)
        {
            _steps[i].Number = i + 1;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still open somewhere; the next "empty" removes it.
        }
    }
}
