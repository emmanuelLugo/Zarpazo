using System.IO;
using System.Runtime.InteropServices;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Reading a video's pictures on another thread, ahead of the one being drawn.</summary>
[Collection("Recording")]
public sealed class FrameReadAheadTests : IClassFixture<SampleVideos>
{
    private readonly SampleVideos _samples;

    public FrameReadAheadTests(SampleVideos samples) => _samples = samples;

    [Fact]
    public void It_hands_over_the_same_pictures_at_the_same_times_as_reading_them_one_by_one()
    {
        const int Width = 320, Height = 180;
        var expected = new List<(TimeSpan Time, byte[] Pixels)>();
        using (var reader = new VideoFrameReader(_samples.Silent, Width, Height))
        {
            var picture = new byte[Width * Height * 4];
            while (reader.TryRead(picture, out TimeSpan time))
            {
                expected.Add((time, (byte[])picture.Clone()));
            }
        }

        using var ahead = new FrameReadAhead(_samples.Silent, Width, Height);
        int count = 0;
        while (ahead.TryTake(out byte[] pixels, out TimeSpan time))
        {
            Assert.Equal(expected[count].Time, time);
            Assert.True(expected[count].Pixels.AsSpan().SequenceEqual(pixels), $"picture {count} differs");
            ahead.Give(pixels);
            count++;
        }

        Assert.Equal(expected.Count, count);
        Assert.True(count > 0);
    }

    [Fact]
    public void A_file_that_cannot_be_read_fails_when_the_first_picture_is_asked_for()
    {
        using var ahead = new FrameReadAhead(Path.Combine(Path.GetTempPath(), "does-not-exist.mp4"), 320, 180);

        Assert.ThrowsAny<Exception>(() => ahead.TryTake(out _, out _));
    }

    [Fact]
    public void It_can_be_dropped_before_the_video_ends_without_hanging()
    {
        var ahead = new FrameReadAhead(_samples.Silent, 320, 180);
        Assert.True(ahead.TryTake(out _, out _));

        ahead.Dispose(); // the reading thread is waiting for a free buffer; this must stop it
    }
}
