using System.IO;
using Windows.Media.Editing;
using Windows.Storage;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

[Collection("Recording")]
public sealed class VideoTrimmerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoTrimmerTests() => Directory.CreateDirectory(_folder);

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

    // Three seconds of a picture that changes, with a tone, so the cut has something to cut.
    private async Task<string> MakeVideoAsync(string name, bool withSound)
    {
        string path = Path.Combine(_folder, name);
        var writer = new Mp4Writer(path, 320, 180, fps: 30, withAudio: withSound);
        writer.Start();
        for (int frame = 0; frame < 90; frame++)
        {
            var pixels = new byte[320 * 180 * 4];
            Array.Fill(pixels, (byte)(frame * 2));
            writer.AddVideo(pixels, TimeSpan.FromSeconds(frame / 30.0));
            if (withSound && frame % 3 == 2)
            {
                var pcm = new byte[Mp4Writer.AudioSampleRate * 100 / 1000 * 4];
                for (int i = 0; i < pcm.Length; i += 2)
                {
                    BitConverter.GetBytes((short)(Math.Sin(i * 0.05) * 6000)).CopyTo(pcm, i);
                }
                writer.AddAudio(pcm, TimeSpan.FromSeconds((frame - 2) / 30.0));
            }
            await Task.Delay(33); // real time: written faster, the queue would drop pictures and shorten the video
        }
        await writer.FinishAsync();
        return path;
    }

    private static async Task<MediaClip> OpenAsync(string path) =>
        await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));

    [Fact]
    public async Task Only_the_chosen_part_is_kept_and_the_picture_size_does_not_change()
    {
        string source = await MakeVideoAsync("source.mp4", withSound: false);
        string target = Path.Combine(_folder, "cut.mp4");

        await VideoTrimmer.TrimAsync(source, target, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        MediaClip clip = await OpenAsync(target);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 0.8, 1.3);
        Assert.Equal((320u, 180u), (clip.GetVideoEncodingProperties().Width, clip.GetVideoEncodingProperties().Height));
        Assert.True(File.Exists(source), "the original is left as it was");
    }

    [Fact]
    public async Task The_sound_track_survives_the_cut()
    {
        string source = await MakeVideoAsync("with-sound.mp4", withSound: true);
        string target = Path.Combine(_folder, "cut-sound.mp4");

        await VideoTrimmer.TrimAsync(source, target, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.0));

        MediaClip clip = await OpenAsync(target);
        Assert.Single(clip.EmbeddedAudioTracks);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.2, 1.8);
    }

    [Fact]
    public async Task Cutting_off_only_the_start_or_only_the_end_works()
    {
        string source = await MakeVideoAsync("source2.mp4", withSound: false);

        await VideoTrimmer.TrimAsync(source, Path.Combine(_folder, "no-start.mp4"), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10)); // end beyond the video
        await VideoTrimmer.TrimAsync(source, Path.Combine(_folder, "no-end.mp4"), TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.InRange((await OpenAsync(Path.Combine(_folder, "no-start.mp4"))).OriginalDuration.TotalSeconds, 1.2, 1.8);
        Assert.InRange((await OpenAsync(Path.Combine(_folder, "no-end.mp4"))).OriginalDuration.TotalSeconds, 0.8, 1.3);
    }

    [Fact]
    public async Task An_empty_or_backwards_range_is_refused()
    {
        string source = await MakeVideoAsync("source3.mp4", withSound: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            VideoTrimmer.TrimAsync(source, Path.Combine(_folder, "x.mp4"), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Progress_is_reported_up_to_the_end()
    {
        string source = await MakeVideoAsync("source4.mp4", withSound: false);
        var seen = new List<double>();

        await VideoTrimmer.TrimAsync(source, Path.Combine(_folder, "p.mp4"), TimeSpan.Zero, TimeSpan.FromSeconds(2), new Progress<double>(seen.Add));
        await Task.Delay(200); // Progress posts to its own context

        Assert.All(seen, value => Assert.InRange(value, 0, 1));
    }

    // ---------- the range rules

    [Theory]
    [InlineData(1.0, 5.0, 10.0, true, 1.0, 5.0)]    // already fine
    [InlineData(-3.0, 5.0, 10.0, true, 0.0, 5.0)]   // before the start of the video
    [InlineData(1.0, 99.0, 10.0, false, 1.0, 10.0)] // past the end
    [InlineData(4.9, 5.0, 10.0, true, 4.5, 5.0)]    // dragging the start into the end pushes it back
    [InlineData(5.0, 5.2, 10.0, false, 5.0, 5.5)]   // dragging the end into the start pushes it out
    public void The_start_and_end_stay_inside_the_video_and_apart(double start, double end, double duration, bool movingStart, double expectedStart, double expectedEnd)
    {
        (double s, double e) = TrimRange.Clamp(start, end, duration, movingStart);

        Assert.Equal(expectedStart, s, 3);
        Assert.Equal(expectedEnd, e, 3);
    }

    [Fact]
    public void A_video_too_short_to_cut_keeps_everything()
    {
        Assert.Equal((0.0, 0.3), TrimRange.Clamp(0.1, 0.2, 0.3, true));
    }

    [Fact]
    public void The_end_stops_at_the_end_of_the_video_even_when_the_start_is_dragged_up_to_it()
    {
        (double s, double e) = TrimRange.Clamp(9.9, 10.0, 10.0, true);

        Assert.Equal(10.0, e, 3);
        Assert.Equal(9.5, s, 3);
    }

    [Fact]
    public void The_trim_window_can_be_created_without_failing_while_its_sliders_load()
    {
        Sta.Run(() =>
        {
            var window = new TrimWindow(Path.Combine(_folder, "not-there.mp4")); // opening the file fails later, asynchronously

            Assert.False(window.SaveButton.IsEnabled); // nothing can be saved before the video has opened
            Assert.Null(window.TrimmedPath);
            window.Close();
        });
    }
}
