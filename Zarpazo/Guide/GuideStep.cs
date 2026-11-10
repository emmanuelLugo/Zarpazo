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

    public BitmapImage Thumbnail => _thumbnail ??= Load(ThumbnailWidth);

    /// <summary>The full picture, read into memory so the file is not left locked.</summary>
    public BitmapImage LoadImage() => Load(0);

    private BitmapImage Load(int decodeWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0)
        {
            image.DecodePixelWidth = decodeWidth;
        }
        image.UriSource = new Uri(imagePath);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public byte[] ReadPng() => File.ReadAllBytes(imagePath);
}
