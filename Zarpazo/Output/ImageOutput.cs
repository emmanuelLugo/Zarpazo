using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Zarpazo.Settings;

namespace Zarpazo.Output;

internal static class ImageOutput
{
    private static readonly TimeSpan PasteFileLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Puts the image on the clipboard twice: as pixels, for editors and chats, and as a PNG file, which is what a terminal
    /// needs to paste a path it can read (the same thing Explorer's "copy file" does).
    /// </summary>
    public static bool CopyToClipboard(BitmapSource image, bool alsoAsFile)
    {
        var data = new DataObject();
        data.SetImage(image);
        if (alsoAsFile)
        {
            string path = SaveForPaste(image, Path.Combine(Path.GetTempPath(), "Zarpazo"));
            data.SetFileDropList(new StringCollection { path });
        }
        Clipboard.SetDataObject(data, copy: true); // copy: keep it after the app closes the overlay
        return true;
    }

    /// <summary>Writes the PNG the clipboard will point to and removes the ones from earlier captures that are old enough.</summary>
    internal static string SaveForPaste(BitmapSource image, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string old in Directory.EnumerateFiles(folder, "*.png"))
        {
            try
            {
                if (DateTime.Now - File.GetLastWriteTime(old) > PasteFileLifetime)
                {
                    File.Delete(old);
                }
            }
            catch (IOException)
            {
                // Still open somewhere; it goes next time.
            }
        }

        // No spaces, so the pasted path needs no quotes.
        string path = Path.Combine(folder, $"Zarpazo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
        Write(path, image);
        return path;
    }

    /// <summary>Asks for a file and writes the image as PNG or JPG. Returns false if the user cancels.</summary>
    public static bool Save(BitmapSource image, Window owner, AppSettings settings)
    {
        bool jpg = settings.SaveFormat == "jpg";
        var dialog = new SaveFileDialog
        {
            Title = "Guardar captura",
            FileName = FileNameTemplate.Expand(settings.FileNameTemplate, DateTime.Now, settings.AutoSaveCounter + 1),
            Filter = "Imagen PNG (*.png)|*.png|Imagen JPG (*.jpg)|*.jpg",
            FilterIndex = jpg ? 2 : 1,
            AddExtension = true,
            DefaultExt = jpg ? ".jpg" : ".png",
            InitialDirectory = Directory.Exists(settings.SaveFolder)
                ? settings.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return false;
        }

        Write(dialog.FileName, image, settings.JpegQuality);
        return true;
    }

    internal static void Write(string path, BitmapSource image, int jpegQuality = AppSettings.DefaultJpegQuality)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = jpegQuality }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
