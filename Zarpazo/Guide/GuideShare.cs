using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Zarpazo.Output;
using Zarpazo.Settings;

namespace Zarpazo.Guide;

/// <summary>
/// Sends the group out: onto the clipboard as files ("Captura 1.png", "Captura 2.png"...), the way Explorer's "copy" does, so
/// one paste attaches all of them in WhatsApp, Teams, Outlook or Gmail; or as one tall image, which any program takes.
/// With more than one capture each one carries its number in the corner, if numbering is on.
/// </summary>
internal static class GuideShare
{
    public static string DefaultFolder { get; } = Path.Combine(Path.GetTempPath(), "Zarpazo", "GuideCopy");

    /// <summary>Copies the group the way the settings say (the shortcut and "Copiar todas" use this); returns how many captures.</summary>
    /// <exception cref="IOException">A file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    /// <exception cref="System.Runtime.InteropServices.COMException">Another program holds the clipboard.</exception>
    public static int Copy(IReadOnlyList<GuideStep> steps, AppSettings settings)
    {
        if (settings.GroupCopyAsOneImage)
        {
            CopyAsOneImage(steps, settings.GroupNumbering, settings.CopyAsFile);
        }
        else
        {
            CopyFiles(steps, DefaultFolder, settings.GroupNumbering);
        }
        return steps.Count;
    }

    public static void CopyFiles(IReadOnlyList<GuideStep> steps, string folder, bool numbered)
    {
        var list = new StringCollection();
        list.AddRange([.. WriteFiles(steps, folder, numbered)]);
        var data = new DataObject();
        data.SetFileDropList(list);
        Clipboard.SetDataObject(data, copy: true); // copy: stays after Zarpazo closes
    }

    public static void CopyAsOneImage(IReadOnlyList<GuideStep> steps, bool numbered, bool alsoAsFile) =>
        ImageOutput.CopyToClipboard(Stack(steps, numbered), alsoAsFile);

    /// <summary>All the captures joined into one image.</summary>
    internal static BitmapSource Stack(IReadOnlyList<GuideStep> steps, bool numbered) =>
        GuideStack.Join(steps.Select(step => Picture(step, numbered && steps.Count > 1)).ToList());

    /// <summary>The pictures as files in an empty folder, named after their number. Files from an earlier copy are removed first.</summary>
    internal static List<string> WriteFiles(IReadOnlyList<GuideStep> steps, string folder, bool numbered)
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
            string path = Path.Combine(folder, $"Captura {step.Number}.png");
            if (numbered && steps.Count > 1)
            {
                ImageOutput.Write(path, NumberBadge.Apply(step.LoadImage(), step.Number));
            }
            else
            {
                File.Copy(step.ImagePath, path, overwrite: true);
            }
            paths.Add(path);
        }
        return paths;
    }

    private static BitmapSource Picture(GuideStep step, bool numbered) =>
        numbered ? NumberBadge.Apply(step.LoadImage(), step.Number) : step.LoadImage();
}
