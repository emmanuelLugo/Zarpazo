using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Decoding a video's pictures on the graphics card.</summary>
[Collection("Recording")]
public sealed class VideoFrameReaderTests : IClassFixture<SampleVideos>
{
    private readonly SampleVideos _samples;

    public VideoFrameReaderTests(SampleVideos samples) => _samples = samples;

    [NeedsGpuFact]
    public void The_graphics_card_gives_the_same_pictures_at_the_same_times_as_the_processor()
    {
        const int Width = 320, Height = 180; // 180 rows decode as 192: the padding must be left out the same way
        using var processor = new VideoFrameReader(_samples.Silent, Width, Height, hardware: false);
        using var card = new VideoFrameReader(_samples.Silent, Width, Height);
        Assert.True(card.OnGpu);
        var expected = new byte[Width * Height * 4];
        var actual = new byte[Width * Height * 4];

        int count = 0;
        while (processor.TryRead(expected, out TimeSpan expectedTime))
        {
            Assert.True(card.TryRead(actual, out TimeSpan time), $"picture {count} is missing");
            Assert.Equal(expectedTime, time);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"picture {count} differs");
            count++;
        }

        Assert.False(card.TryRead(actual, out _));
        Assert.True(count > 0);
    }
}
