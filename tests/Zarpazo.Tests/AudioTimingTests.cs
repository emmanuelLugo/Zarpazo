using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class AudioTimingTests
{
    private const int BytesPerMs = 192; // 48 frames of 2 channels of 2 bytes
    private static byte[] Chunk(int ms = 20) => Enumerable.Range(0, ms * BytesPerMs).Select(i => (byte)(i % 251)).ToArray();

    [Fact]
    public void A_later_offset_moves_the_chunk_later_unchanged()
    {
        byte[] pcm = Chunk();

        var (moved, time) = AudioTiming.Shift(pcm, startMs: 100, offsetMs: 50)!.Value;

        Assert.Equal(150, time);
        Assert.Same(pcm, moved);
    }

    [Fact]
    public void An_earlier_offset_moves_the_chunk_earlier()
    {
        Assert.Equal(60, AudioTiming.Shift(Chunk(), startMs: 100, offsetMs: -40)!.Value.TimeMs);
    }

    [Fact]
    public void A_chunk_that_would_start_before_zero_loses_its_first_part_instead()
    {
        byte[] pcm = Chunk();

        var (trimmed, time) = AudioTiming.Shift(pcm, startMs: 0, offsetMs: -10)!.Value;

        Assert.Equal(0, time);
        Assert.Equal(10 * BytesPerMs, trimmed.Length); // the last 10 ms of the 20
        Assert.Equal(pcm[10 * BytesPerMs], trimmed[0]);
    }

    [Fact]
    public void A_chunk_that_ends_before_zero_is_dropped()
    {
        Assert.Null(AudioTiming.Shift(Chunk(), startMs: 0, offsetMs: -30));
        Assert.Null(AudioTiming.Shift(Chunk(), startMs: 0, offsetMs: -20)); // ends exactly at zero: nothing left
    }

    [Fact]
    public void Chunks_moved_earlier_still_join_up_with_no_gap_and_no_overlap()
    {
        // Three 20 ms chunks moved 30 ms earlier: the first is dropped, the second is cut to 10 ms, the third follows right after.
        var placed = new List<(int Length, long Time)>();
        for (int i = 0; i < 3; i++)
        {
            if (AudioTiming.Shift(Chunk(), i * 20, -30) is var (pcm, time))
            {
                placed.Add((pcm.Length / BytesPerMs, time));
            }
        }

        Assert.Equal([(10, 0L), (20, 10L)], placed);
        Assert.Equal(placed[0].Time + placed[0].Length, placed[1].Time); // contiguous
    }

    [Fact]
    public void Silence_has_the_length_asked_for_and_nothing_in_it()
    {
        byte[] silence = AudioTiming.Silence(50);

        Assert.Equal(50 * BytesPerMs, silence.Length);
        Assert.All(silence, b => Assert.Equal(0, b));
    }
}
