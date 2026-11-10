using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.History;

/// <summary>
/// The finished captures, kept as PNG files in one folder. File names sort by time, so the name is the order.
/// A capture that can be opened again has two more files with the same name: <c>.json</c> (<see cref="EditState"/>) and,
/// when annotations were drawn, <c>.clean</c> (the same area without them). Once its text has been read for the search,
/// <c>.txt</c> holds it (empty when the picture has none, so it is not read again).
/// </summary>
internal sealed class CaptureHistory
{
    private const string StateExtension = ".json";
    private const string CleanExtension = ".clean";
    private const string TextExtension = ".txt";
    private const string KeepExtension = ".keep";
    private const string NamePrefix = "Zarpazo_";
    private const string StampFormat = "yyyy-MM-dd_HH-mm-ss-fff";
    private const int StampLength = 8 + 23; // "Zarpazo_" and the time that follows it
    private static readonly string[] RecordingExtensions = [".gif", ".mp4"];

    /// <summary>A video can be hundreds of megabytes, so recordings are kept in far smaller numbers than captures.</summary>
    public const int RecordingLimit = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zarpazo", "History");

    public CaptureHistory(string folder) => _folder = folder;

    public string Folder => _folder;

    /// <summary>Stores the image and drops the oldest ones beyond <paramref name="limit"/>.</summary>
    /// <param name="state">Makes the capture editable again. Without it the capture is only a picture.</param>
    /// <param name="clean">The area without annotations, for editing them later. Without it the picture itself is the base.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public string Add(BitmapSource image, int limit, EditState? state = null, BitmapSource? clean = null)
    {
        Directory.CreateDirectory(_folder);
        string stamp = $"Zarpazo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
        string path = Path.Combine(_folder, stamp + ".png");
        for (int copy = 1; File.Exists(path); copy++)
        {
            path = Path.Combine(_folder, $"{stamp}_{copy}.png"); // same millisecond: "_1" still sorts after the plain name
        }

        ImageOutput.Write(path, image);
        if (clean != null)
        {
            ImageOutput.Write(Path.ChangeExtension(path, CleanExtension), clean); // not a .png name, so List ignores it
        }
        if (state != null)
        {
            File.WriteAllText(Path.ChangeExtension(path, StateExtension), JsonSerializer.Serialize(state, JsonOptions));
        }
        Prune(limit);
        return path;
    }

    /// <summary>
    /// Keeps a finished GIF or video: a copy of the file, and a PNG with the same name (its first frame) that makes it show in the list.
    /// Drops the oldest recordings beyond <paramref name="recordingLimit"/>; captures are not counted.
    /// </summary>
    /// <exception cref="IOException">A file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public string AddRecording(string mediaFile, BitmapSource firstFrame, int recordingLimit = RecordingLimit)
    {
        Directory.CreateDirectory(_folder);
        string stamp = $"Zarpazo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
        string extension = Path.GetExtension(mediaFile);
        string path = Path.Combine(_folder, stamp + ".png");
        for (int copy = 1; File.Exists(path); copy++)
        {
            path = Path.Combine(_folder, $"{stamp}_{copy}.png");
        }

        // The recording first: the picture is what makes the entry appear, so a half-made entry never shows.
        File.Copy(mediaFile, Path.ChangeExtension(path, extension));
        ImageOutput.Write(path, firstFrame);

        foreach (HistoryEntry old in List().Where(entry => entry.IsRecording && !entry.Kept).Skip(Math.Max(recordingLimit, 0)))
        {
            try
            {
                Delete(old.Path);
            }
            catch (IOException)
            {
                // Still open somewhere; it goes next time.
            }
        }
        return path;
    }

    /// <summary>Newest first. Empty if nothing was captured yet.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(_folder, "Zarpazo_*.png")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Select(path => new HistoryEntry(
                path,
                File.GetLastWriteTime(path),
                File.Exists(Path.ChangeExtension(path, StateExtension)),
                RecordingExtensions.Select(extension => Path.ChangeExtension(path, extension)).FirstOrDefault(File.Exists),
                File.Exists(Path.ChangeExtension(path, KeepExtension))))
            .ToList();
    }

    /// <summary>
    /// Deletes the captures and recordings taken more than <paramref name="days"/> days before <paramref name="now"/>. The age is the
    /// time in the file name; a name that does not have one is left alone. A file open elsewhere is kept for the next time.
    /// </summary>
    public void DeleteOlderThan(int days, DateTime now)
    {
        if (days < 1)
        {
            return;
        }

        DateTime cutoff = now.AddDays(-days);
        foreach (HistoryEntry entry in List())
        {
            string name = Path.GetFileNameWithoutExtension(entry.Path);
            if (name.StartsWith(NamePrefix, StringComparison.Ordinal) && name.Length >= StampLength
                && DateTime.TryParseExact(name.AsSpan(NamePrefix.Length, StampLength - NamePrefix.Length), StampFormat, null, System.Globalization.DateTimeStyles.None, out DateTime taken)
                && taken < cutoff && !entry.Kept)
            {
                try
                {
                    Delete(entry.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still open somewhere; it goes next time.
                }
            }
        }
    }

    /// <summary>Removes the capture and the files that belong to it.</summary>
    public void Delete(string path)
    {
        var files = new List<string>
        {
            path,
            Path.ChangeExtension(path, StateExtension),
            Path.ChangeExtension(path, CleanExtension),
            Path.ChangeExtension(path, TextExtension),
            Path.ChangeExtension(path, KeepExtension),
        };
        files.AddRange(RecordingExtensions.Select(extension => Path.ChangeExtension(path, extension)));
        foreach (string file in files)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>Whether the capture is kept: the limits and the age never delete it, only the person does.</summary>
    public bool IsKept(string path) => File.Exists(Path.ChangeExtension(path, KeepExtension));

    /// <exception cref="IOException">The mark could not be written or removed.</exception>
    public void SetKept(string path, bool kept)
    {
        string mark = Path.ChangeExtension(path, KeepExtension);
        if (kept && File.Exists(path))
        {
            File.WriteAllText(mark, "");
        }
        else if (!kept && File.Exists(mark))
        {
            File.Delete(mark);
        }
    }

    /// <summary>The text read from the picture. Null when it has not been read yet; empty when it has none.</summary>
    public string? ReadText(string path)
    {
        try
        {
            string textPath = Path.ChangeExtension(path, TextExtension);
            return File.Exists(textPath) ? File.ReadAllText(textPath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps the text read from a picture. Does nothing if the capture was deleted in the meantime.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void WriteText(string path, string text)
    {
        if (File.Exists(path))
        {
            File.WriteAllText(Path.ChangeExtension(path, TextExtension), text);
        }
    }

    /// <summary>Whether <paramref name="text"/> has <paramref name="query"/>, ignoring case and accents. An empty query matches everything.</summary>
    public static bool Matches(string? text, string query)
    {
        query = query.Trim();
        return query.Length == 0
            || (text != null && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                text, query, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0);
    }

    /// <summary>The saved state and the image to edit on top of. Null if the capture can't be opened again.</summary>
    public (EditState State, BitmapSource Base)? LoadEditable(string path)
    {
        string statePath = Path.ChangeExtension(path, StateExtension);
        string cleanPath = Path.ChangeExtension(path, CleanExtension);
        try
        {
            EditState? state = JsonSerializer.Deserialize<EditState>(File.ReadAllText(statePath));
            if (state is not { Width: > 0, Height: > 0 })
            {
                return null;
            }
            return (state, Load(File.Exists(cleanPath) ? cleanPath : path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or FileFormatException)
        {
            return null; // missing or damaged: the capture is still usable as a picture
        }
    }

    /// <summary>Reads the whole image, without keeping the file open.</summary>
    public static BitmapSource Load(string path) => Decode(path, thumbnailWidth: 0);

    /// <summary>A small version for the list, decoded at that size so a hundred of them stay cheap.</summary>
    public static BitmapSource LoadThumbnail(string path, int width) => Decode(path, width);

    private static BitmapSource Decode(string path, int thumbnailWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad; // read it now, so the file can be deleted while it is on screen
        if (thumbnailWidth > 0)
        {
            image.DecodePixelWidth = thumbnailWidth;
        }
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Prune(int limit)
    {
        foreach (HistoryEntry old in List().Where(entry => !entry.Kept).Skip(Math.Max(limit, 0)))
        {
            try
            {
                Delete(old.Path);
            }
            catch (IOException)
            {
                // Still open somewhere; it goes next time.
            }
        }
    }
}
