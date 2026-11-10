using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace Zarpazo.Guide;

/// <summary>
/// Puts every picture of the guide on the clipboard as files ("Paso 1.png", "Paso 2.png"...), the way Explorer's "copy" does.
/// One paste then attaches all of them in WhatsApp, Teams, Outlook, Gmail and the like.
/// </summary>
internal static class GuideShare
{
    public static string DefaultFolder { get; } = Path.Combine(Path.GetTempPath(), "Zarpazo", "GuideCopy");

    /// <summary>Writes the files and puts them on the clipboard; returns the paths, in step order.</summary>
    /// <exception cref="IOException">A file could not be written.</exception>
    /// <exception cref="System.Runtime.InteropServices.COMException">Another program holds the clipboard.</exception>
    public static IReadOnlyList<string> CopyFiles(IReadOnlyList<GuideStep> steps, string folder)
    {
        List<string> paths = WriteFiles(steps, folder);
        var list = new StringCollection();
        list.AddRange([.. paths]);
        var data = new DataObject();
        data.SetFileDropList(list);
        Clipboard.SetDataObject(data, copy: true); // copy: stays after Zarpazo closes
        return paths;
    }

    /// <summary>The pictures as files in an empty folder, named after their step. Files from an earlier copy are removed first.</summary>
    internal static List<string> WriteFiles(IReadOnlyList<GuideStep> steps, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string old in Directory.EnumerateFiles(folder, "*.png"))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Still held by whatever the last paste went to; it is overwritten below if the name is the same.
            }
        }

        var paths = new List<string>();
        foreach (GuideStep step in steps)
        {
            string path = Path.Combine(folder, $"Paso {step.Number}.png");
            File.Copy(step.ImagePath, path, overwrite: true);
            paths.Add(path);
        }
        return paths;
    }
}
