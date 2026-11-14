using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Saving a video with a blur over it: the pictures are made again and the blur is there only while it lasts.</summary>
[Collection("Recording")]
public sealed class VideoOverlayExportTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 180;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoOverlayExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // Three seconds of a still picture: the left half white, the right half black, with a hard edge between them.
    private async Task<string> EdgeVideoAsync()
    {
        string path = Path.Combine(_folder, "edge.mp4");
        var writer = new Mp4Writer(path, Width, Height, fps: 30, withAudio: false, live: false);
        writer.Start();
        for (int frame = 0; frame < 90; frame++)
        {
            byte[] pixels = writer.RentPicture();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = (y * Width + x) * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = x < Width / 2 ? (byte)255 : (byte)0;
                    pixels[i + 3] = 255;
                }
            }
            await writer.AddVideoAsync(pixels, TimeSpan.FromSeconds(frame / 30.0));
        }
        await writer.FinishAsync();
        return path;
    }

    // The green of one pixel of the picture at a moment, in whole pixels.
    private static async Task<int> PixelAsync(string path, double seconds, int x, int y)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        using IRandomAccessStream stream = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(seconds), Width, Height, VideoFramePrecision.NearestFrame);
        var decoder = BitmapDecoder.Create(stream.AsStreamForRead(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource picture = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        picture.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[1];
    }

    [Fact]
    public async Task A_saved_video_has_the_blur_only_while_it_lasts()
    {
        string source = await EdgeVideoAsync();
        string target = Path.Combine(_folder, "blurred.mp4");
        var blur = new VideoOverlay(OverlayKind.Blur, 1, 2, 0.35, 0.25, 0.65, 0.75);

        await VideoExporter.ExportAsync(source, target, [new VideoSegment(0, 3)], [], 3, frame: null, overlays: [blur]);

        int before = await PixelAsync(target, 0.4, Width / 2 + 1, Height / 2);
        int during = await PixelAsync(target, 1.5, Width / 2 + 1, Height / 2);
        int outside = await PixelAsync(target, 1.5, Width / 4, 10);
        int after = await PixelAsync(target, 2.5, Width / 2 + 1, Height / 2);

        Assert.InRange(before, 0, 40);   // black, right of the edge
        Assert.InRange(during, 70, 200); // the edge is smeared over it
        Assert.InRange(outside, 215, 255);
        Assert.InRange(after, 0, 40);
    }
}
