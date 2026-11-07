using System.IO;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.History;

/// <summary>The finished captures, kept as PNG files in one folder. File names sort by time, so the name is the order.</summary>
internal sealed class CaptureHistory
{
    private readonly string _folder;

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zarpazo", "History");

    public CaptureHistory(string folder) => _folder = folder;

    public string Folder => _folder;

    /// <summary>Stores the image and drops the oldest ones beyond <paramref name="limit"/>.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public string Add(BitmapSource image, int limit)
    {
        Directory.CreateDirectory(_folder);
        string stamp = $"Zarpazo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
        string path = Path.Combine(_folder, stamp + ".png");
        for (int copy = 1; File.Exists(path); copy++)
        {
            path = Path.Combine(_folder, $"{stamp}_{copy}.png"); // same millisecond: "_1" still sorts after the plain name
        }
        ImageOutput.Write(path, image);
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
            .Select(path => new HistoryEntry(path, File.GetLastWriteTime(path)))
            .ToList();
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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
