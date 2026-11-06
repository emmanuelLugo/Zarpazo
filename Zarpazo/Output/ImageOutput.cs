using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Zarpazo.Output;

internal static class ImageOutput
{
    public static bool CopyToClipboard(BitmapSource image)
    {
        Clipboard.SetImage(image);
        return true;
    }

    /// <summary>Asks for a file and writes the image as PNG or JPG. Returns false if the user cancels.</summary>
    public static bool Save(BitmapSource image, Window owner)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar captura",
            FileName = $"Zarpazo {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
            Filter = "Imagen PNG (*.png)|*.png|Imagen JPG (*.jpg)|*.jpg",
            AddExtension = true,
            DefaultExt = ".png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return false;
        }

        BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using FileStream file = File.Create(dialog.FileName);
        encoder.Save(file);
        return true;
    }
}
