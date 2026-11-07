using System.IO;
using System.Windows.Media.Imaging;
using Zarpazo.Settings;

namespace Zarpazo.Output;

/// <summary>Saves captures and recordings to a folder by itself, naming them from the template in the settings.</summary>
internal sealed class AutoSaver(SettingsStore settings)
{
    public bool IsEnabled => settings.Current.AutoSave;

    /// <summary>The chosen folder, or Pictures\Zarpazo when none was chosen.</summary>
    public static string FolderFor(AppSettings current) =>
        string.IsNullOrWhiteSpace(current.AutoSaveFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Zarpazo")
            : current.AutoSaveFolder;

    /// <summary>Writes the picture as PNG or JPG, as the settings say, and returns where.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public string SaveImage(BitmapSource image)
    {
        string path = NextPath(settings.Current.SaveFormat == "jpg" ? ".jpg" : ".png");
        ImageOutput.Write(path, image);
        return path;
    }

    /// <summary>Copies a finished GIF or video, keeping its extension, and returns where.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public string SaveFile(string file)
    {
        string path = NextPath(Path.GetExtension(file));
        File.Copy(file, path);
        return path;
    }

    // Each save takes the next number, which is kept so that it carries on after the app is closed.
    private string NextPath(string extension)
    {
        AppSettings current = settings.Current;
        current.AutoSaveCounter++;
        string folder = FolderFor(current);
        Directory.CreateDirectory(folder);
        string path = FileNameTemplate.UniquePath(folder, FileNameTemplate.Expand(current.FileNameTemplate, DateTime.Now, current.AutoSaveCounter), extension);
        try
        {
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The number is only a convenience: failing to remember it must not lose the capture.
        }
        return path;
    }
}
