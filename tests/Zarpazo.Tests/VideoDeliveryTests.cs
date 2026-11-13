using System.IO;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class VideoDeliveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoDeliveryTests() => Directory.CreateDirectory(_folder);

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

    private VideoDelivery New() => new(Path.Combine(_folder, "rec.original.mp4"), Path.Combine(_folder, "rec.mp4"));

    [Fact]
    public async Task The_finished_video_is_made_again_only_when_a_choice_changed()
    {
        VideoDelivery delivery = New();
        int made = 0;
        Task Make(string target)
        {
            made++;
            File.WriteAllText(target, $"version {made}");
            return Task.CompletedTask;
        }

        string first = await delivery.PrepareAsync("zoom 2x", Make);
        await delivery.PrepareAsync("zoom 2x", Make);
        Assert.Equal(1, made);
        Assert.True(delivery.Delivered);
        Assert.Equal(Path.Combine(_folder, "rec.mp4"), first);

        await delivery.PrepareAsync("zoom 3x", Make);
        Assert.Equal(2, made);
        Assert.Equal("version 2", File.ReadAllText(first));
        Assert.True(delivery.IsCurrent("zoom 3x"));
        Assert.False(delivery.IsCurrent("zoom 2x"));
    }

    [Fact]
    public async Task When_making_it_fails_the_video_made_before_is_kept_whole()
    {
        VideoDelivery delivery = New();
        await delivery.PrepareAsync("a", target => File.WriteAllTextAsync(target, "good"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.PrepareAsync("b", async target =>
        {
            await File.WriteAllTextAsync(target, "half");
            throw new InvalidOperationException("Windows could not");
        }));

        Assert.Equal("good", File.ReadAllText(Path.Combine(_folder, "rec.mp4")));
        Assert.True(delivery.IsCurrent("a"));
        Assert.Single(Directory.GetFiles(_folder)); // nothing half-made left behind
    }

    [Fact]
    public void A_recording_keeps_its_clean_copy_under_a_name_of_its_own()
    {
        Assert.Equal(Path.Combine(_folder, "Zarpazo_1.original.mp4"), RecordingFiles.OriginalFor(Path.Combine(_folder, "Zarpazo_1.mp4")));
        Assert.Equal("Zarpazo_1", RecordingFiles.WithoutOriginalSuffix("Zarpazo_1.original"));
        Assert.Equal("Zarpazo_1", RecordingFiles.WithoutOriginalSuffix("Zarpazo_1"));
    }
}
