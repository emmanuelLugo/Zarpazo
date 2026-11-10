using System.IO;
using System.Windows.Media.Imaging;

namespace Zarpazo.Guide;

/// <summary>One capture of the group: a PNG file kept on disk (not in memory) and its place in the order.</summary>
internal sealed class GuideStep(string imagePath)
{
    private const int ThumbnailWidth = 220;

    private BitmapImage? _thumbnail;

    public string ImagePath => imagePath;

    /// <summary>Set by <see cref="GuideDocument"/> every time the order changes; starts at 1.</summary>
    public int Number { get; internal set; }

    public string Heading => $"Captura {Number}";

    public BitmapImage Thumbnail => _thumbnail ??= LoadThumbnail();

    private BitmapImage LoadThumbnail()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // read now, so the file is not left locked
        image.DecodePixelWidth = ThumbnailWidth;
        image.UriSource = new Uri(imagePath);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public byte[] ReadPng() => File.ReadAllBytes(imagePath);
}
