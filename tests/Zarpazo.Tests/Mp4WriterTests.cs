using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Editing;
using Windows.Storage;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

[Collection("Recording")]
public sealed class Mp4WriterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public Mp4WriterTests() => Directory.CreateDirectory(_folder);

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

    // A square that moves across a gray picture, so the video has something to encode.
    private static byte[] Picture(int width, int height, int frame)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 90;
            pixels[i + 3] = 255;
        }
        int left = frame * 6 % (width - 40);
        for (int y = 20; y < 60; y++)
        {
            for (int x = left; x < left + 40; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = 40;
                pixels[i + 1] = 80;
                pixels[i + 2] = 240;
            }
        }
        return pixels;
    }

    private static byte[] Tone(int milliseconds, int startMs, double hertz)
    {
        int frames = Mp4Writer.AudioSampleRate * milliseconds / 1000;
        var pcm = new byte[frames * Mp4Writer.AudioChannels * 2];
        for (int n = 0; n < frames; n++)
        {
            double t = (startMs / 1000.0) + n / (double)Mp4Writer.AudioSampleRate;
            short value = (short)(Math.Sin(2 * Math.PI * hertz * t) * 8000);
            for (int channel = 0; channel < Mp4Writer.AudioChannels; channel++)
            {
                BitConverter.GetBytes(value).CopyTo(pcm, (n * Mp4Writer.AudioChannels + channel) * 2);
            }
        }
        return pcm;
    }

    private static async Task<MediaClip> OpenAsync(string path) =>
        await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));

    [Fact]
    public async Task Video_only_frames_become_an_mp4_of_the_right_size_and_length()
    {
        string path = Path.Combine(_folder, "video.mp4");
        var writer = new Mp4Writer(path, 320, 180, fps: 30, withAudio: false);
        writer.Start();
        int accepted = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            if (writer.AddVideo(Picture(320, 180, frame), TimeSpan.FromSeconds(frame / 30.0)))
            {
                accepted++;
            }
            await Task.Delay(33); // real time: the queue drops frames when the encoder is a second behind
        }
        Assert.InRange(accepted, 50, 60); // a loaded machine may start the encoder late and lose a few at the very start

        await writer.FinishAsync();

        MediaClip clip = await OpenAsync(path);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.8, 2.3);
        Assert.Equal((320u, 180u), (clip.GetVideoEncodingProperties().Width, clip.GetVideoEncodingProperties().Height));
        Assert.Empty(clip.EmbeddedAudioTracks);
    }

    [Fact]
    public async Task Video_with_sound_has_an_audio_track()
    {
        string path = Path.Combine(_folder, "both.mp4");
        var writer = new Mp4Writer(path, 320, 180, fps: 30, withAudio: true);
        writer.Start();
        for (int frame = 0; frame < 60; frame++)
        {
            writer.AddVideo(Picture(320, 180, frame), TimeSpan.FromSeconds(frame / 30.0));
        }
        for (int chunk = 0; chunk < 100; chunk++)
        {
            writer.AddAudio(Tone(20, chunk * 20, 440), TimeSpan.FromMilliseconds(chunk * 20));
        }

        await writer.FinishAsync();

        MediaClip clip = await OpenAsync(path);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.8, 2.3);
        Assert.Single(clip.EmbeddedAudioTracks);
    }

    [Fact]
    public void An_odd_picture_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new Mp4Writer(Path.Combine(_folder, "x.mp4"), 321, 180, 30, false));
    }

    [Fact]
    public async Task When_the_encoder_is_far_behind_extra_frames_are_dropped_instead_of_piling_up()
    {
        var writer = new Mp4Writer(Path.Combine(_folder, "flood.mp4"), 320, 180, fps: 30, withAudio: false);
        // Not started: nothing takes frames out, so the queue fills and then refuses.
        int accepted = 0;
        for (int frame = 0; frame < 100; frame++)
        {
            if (writer.AddVideo(Picture(320, 180, frame), TimeSpan.FromSeconds(frame / 30.0)))
            {
                accepted++;
            }
        }

        Assert.Equal(30, accepted);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_picture_is_the_right_way_up_and_not_mirrored()
    {
        // A solid red block in the top-left corner of a gray picture. If the video came out upside down or mirrored,
        // the block would be at the bottom or on the right.
        const int width = 320;
        const int height = 180;
        byte[] Marked()
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 128;
                pixels[i + 3] = 255;
            }
            for (int y = 0; y < 50; y++)
            {
                for (int x = 0; x < 80; x++)
                {
                    int i = (y * width + x) * 4;
                    pixels[i] = 0;
                    pixels[i + 1] = 0;
                    pixels[i + 2] = 255; // red
                }
            }
            return pixels;
        }

        string path = Path.Combine(_folder, "orientation.mp4");
        var writer = new Mp4Writer(path, width, height, fps: 30, withAudio: false);
        writer.Start();
        for (int frame = 0; frame < 30; frame++)
        {
            writer.AddVideo(Marked(), TimeSpan.FromSeconds(frame / 30.0));
            await Task.Delay(33);
        }
        await writer.FinishAsync();

        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        using var thumbnail = (await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(400), width, height, VideoFramePrecision.NearestFrame)).AsStreamForRead();
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(thumbnail, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var bitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var all = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(all, bitmap.PixelWidth * 4, 0);

        bool IsRed(int x, int y)
        {
            int i = (y * bitmap.PixelWidth + x) * 4;
            return all[i + 2] > 180 && all[i + 1] < 90 && all[i] < 90;
        }

        int w = bitmap.PixelWidth;
        int h = bitmap.PixelHeight;
        Assert.True(IsRed(w * 80 / 320 / 2, h * 25 / 180), "the red block should be at the top left");
        Assert.False(IsRed(w * 40 / 320, h - h * 25 / 180), "it must not be at the bottom left (upside down)");
        Assert.False(IsRed(w - w * 40 / 320, h * 25 / 180), "it must not be at the top right (mirrored)");
    }
}
