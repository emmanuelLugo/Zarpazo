using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.History;

/// <summary>
/// The finished captures, kept as PNG files in one folder. File names sort by time, so the name is the order.
/// A capture that can be opened again has two more files with the same name: <c>.json</c> (<see cref="EditState"/>) and,
/// when annotations were drawn, <c>.clean</c> (the same area without them).
/// </summary>
internal sealed class CaptureHistory
{
    private const string StateExtension = ".json";
    private const string CleanExtension = ".clean";

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

    /// <summary>Newest first. Empty if nothing was captured yet.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(_folder, "Zarpazo_*.png")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Select(path => new HistoryEntry(path, File.GetLastWriteTime(path), File.Exists(Path.ChangeExtension(path, StateExtension))))
            .ToList();
    }

    /// <summary>Removes the capture and the files that belong to it.</summary>
    public void Delete(string path)
    {
        foreach (string file in new[] { path, Path.ChangeExtension(path, StateExtension), Path.ChangeExtension(path, CleanExtension) })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
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
        foreach (HistoryEntry old in List().Skip(Math.Max(limit, 0)))
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
