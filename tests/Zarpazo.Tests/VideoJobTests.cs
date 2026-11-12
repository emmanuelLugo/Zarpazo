using System.Drawing;
using System.IO;
using Windows.Media.Editing;
using Windows.Storage;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Records a real corner of the screen to a video, without sound so that no audio device is needed.</summary>
[Collection("Recording")]
public sealed class VideoJobTests : IDisposable
{
    private static readonly Rectangle Corner = new(0, 0, 321, 181); // odd on purpose: the video has to come out even
    private static readonly RecordingOptions NoSound = new(RecordingFormat.Video, Microphone: false, SystemSound: false);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public async Task It_makes_an_mp4_as_long_as_the_recording_with_an_even_size_and_no_sound_track()
    {
        var job = new VideoJob(Corner, NoSound);
        job.Start(_folder);
        await Task.Delay(2000);

        string? path = await job.FinishAsync();

        Assert.NotNull(path);
        Assert.Equal(".mp4", Path.GetExtension(path));
        MediaClip clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.5, 3.0);
        Assert.Equal((320u, 180u), (clip.GetVideoEncodingProperties().Width, clip.GetVideoEncodingProperties().Height));
        Assert.Empty(clip.EmbeddedAudioTracks);
        CursorTrack? cursor = CursorTrack.Load(path);
        Assert.NotNull(cursor); // where the pointer went, for the zoom of the editor
        Assert.Equal((320, 180), (cursor.Width, cursor.Height));
        Assert.NotEmpty(cursor.Moves);
    }

    [Fact]
    public async Task Throwing_a_recording_away_leaves_no_file_behind()
    {
        var job = new VideoJob(Corner, NoSound);
        job.Start(_folder);
        await Task.Delay(600);

        job.Abort();

        // The file is closed and deleted in the background.
        for (int i = 0; i < 50 && Directory.GetFiles(_folder, "*.mp4").Length > 0; i++)
        {
            await Task.Delay(100);
        }
        Assert.Empty(Directory.GetFiles(_folder, "*.mp4"));
    }

    [Fact]
    public void A_large_area_is_shrunk_to_the_size_limit_keeping_its_proportions()
    {
        Assert.Equal((1920, 1080), RegionGrabber.FitSize(new Rectangle(0, 0, 3840, 2160), VideoJob.MaxSide, even: true));
    }

    [Fact]
    public void The_size_rounds_down_to_even_numbers_for_the_encoder()
    {
        Assert.Equal((320, 180), RegionGrabber.FitSize(new Rectangle(0, 0, 321, 181), VideoJob.MaxSide, even: true));
        Assert.Equal((321, 181), RegionGrabber.FitSize(new Rectangle(0, 0, 321, 181), VideoJob.MaxSide, even: false));
    }

    [Fact]
    public void Sound_choices_only_count_for_a_video()
    {
        Assert.True(new RecordingOptions(RecordingFormat.Video, true, false).HasSound);
        Assert.True(new RecordingOptions(RecordingFormat.Video, false, true).HasSound);
        Assert.False(new RecordingOptions(RecordingFormat.Video, false, false).HasSound);
        Assert.False(new RecordingOptions(RecordingFormat.Gif, true, true).HasSound);
    }

    [Fact]
    public async Task A_paused_stretch_leaves_no_gap_and_no_extra_length_in_the_video()
    {
        var job = new VideoJob(Corner, NoSound);
        job.Start(_folder);
        await Task.Delay(1000);

        job.Pause();
        Assert.True(job.IsPaused);
        await Task.Delay(1500);
        job.Resume();
        await Task.Delay(1000);
        string? path = await job.FinishAsync();

        MediaClip clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path!));
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 1.6, 2.8); // two seconds recorded, not three and a half
    }

    [Fact]
    public void Preparing_does_not_record_and_can_be_thrown_away()
    {
        var job = new VideoJob(Corner, NoSound);

        job.Prepare(_folder);
        job.Abort(); // a countdown that was cancelled

        Assert.Equal(TimeSpan.Zero, job.Elapsed);
        Assert.Empty(Directory.GetFiles(_folder, "*.mp4"));
    }

    [Fact]
    public async Task A_finished_video_hands_over_its_first_picture_for_the_history()
    {
        var job = new VideoJob(Corner, NoSound);
        job.Start(_folder);
        await Task.Delay(700);

        await job.FinishAsync();

        Assert.NotNull(job.Thumbnail);
        Assert.Equal((320, 180), (job.Thumbnail!.PixelWidth, job.Thumbnail.PixelHeight));
        Assert.True(job.Thumbnail.IsFrozen);
    }

    [Fact]
    public async Task A_finished_gif_hands_over_its_first_picture_too()
    {
        var job = new GifJob(Corner);
        job.Start(_folder);
        await Task.Delay(700);

        string? path = await job.FinishAsync();

        Assert.Equal(".gif", Path.GetExtension(path));
        Assert.NotNull(job.Thumbnail);
        Assert.Equal((321, 181), (job.Thumbnail!.PixelWidth, job.Thumbnail.PixelHeight)); // a GIF keeps odd sizes
    }
}
