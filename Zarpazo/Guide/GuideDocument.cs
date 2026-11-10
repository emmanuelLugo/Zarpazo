using System.IO;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.Guide;

/// <summary>
/// The guide being put together: a title and the steps in order. The pictures live in a folder, one PNG each, so a long guide
/// does not sit in memory. It is kept until the person starts a new one or quits.
/// </summary>
internal sealed class GuideDocument(string folder)
{
    private readonly List<GuideStep> _steps = [];

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zarpazo", "Guide");

    public string Title { get; set; } = "";

    public IReadOnlyList<GuideStep> Steps => _steps;

    /// <summary>Adds the picture as the last step.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public GuideStep Add(BitmapSource image)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"{Guid.NewGuid():N}.png");
        ImageOutput.Write(path, image);

        var step = new GuideStep(path);
        _steps.Add(step);
        Renumber();
        return step;
    }

    public void Remove(GuideStep step)
    {
        if (_steps.Remove(step))
        {
            Delete(step.ImagePath);
            Renumber();
        }
    }

    /// <summary>Moves a step up (negative) or down (positive) by that many places, stopping at the ends.</summary>
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
    }

    /// <summary>Starts over: no steps, no title, and the pictures of the old ones deleted (also leftovers from an earlier run).</summary>
    public void Clear()
    {
        _steps.Clear();
        Title = "";
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.png"))
        {
            Delete(file);
        }
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
            // Still open somewhere; the next "start over" removes it.
        }
    }
}
