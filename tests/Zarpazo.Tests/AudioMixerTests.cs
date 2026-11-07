using NAudio.Wave;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class AudioMixerTests
{
    // Gives the same value for as many samples as it is told it has, then nothing: a device that has gone quiet.
    private sealed class Constant(float value, int available = int.MaxValue) : ISampleProvider
    {
        private int _left = available;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _left);
            Array.Fill(buffer, value, offset, n);
            _left -= n;
            return n;
        }
    }

    private static short[] Samples(byte[] pcm) => Enumerable.Range(0, pcm.Length / 2).Select(i => BitConverter.ToInt16(pcm, i * 2)).ToArray();

    [Fact]
    public void A_chunk_is_twenty_milliseconds_of_16_bit_stereo()
    {
        byte[] chunk = new AudioMixer([new Constant(0f)]).NextChunk();

        Assert.Equal(48000 * 20 / 1000 * 2 * 2, chunk.Length);
    }

    [Fact]
    public void One_source_comes_through_at_its_own_level()
    {
        short[] samples = Samples(new AudioMixer([new Constant(0.5f)]).NextChunk());

        Assert.All(samples, s => Assert.InRange(s, 16380, 16390));
    }

    [Fact]
    public void Two_sources_are_added_together()
    {
        short[] samples = Samples(new AudioMixer([new Constant(0.3f), new Constant(0.4f)]).NextChunk());

        Assert.All(samples, s => Assert.InRange(s, 22930, 22940)); // 0.7 of full scale
    }

    [Fact]
    public void Sources_that_add_past_full_scale_are_cut_there_instead_of_wrapping_into_noise()
    {
        short[] samples = Samples(new AudioMixer([new Constant(0.8f), new Constant(0.8f)]).NextChunk());

        Assert.All(samples, s => Assert.Equal(short.MaxValue, s));
    }

    [Fact]
    public void A_source_with_less_than_a_chunk_ready_counts_as_silence_for_the_rest()
    {
        // Windows sends no sound at all while nothing plays; the video must not wait for it.
        short[] samples = Samples(new AudioMixer([new Constant(0.5f, available: 100)]).NextChunk());

        Assert.All(samples.Take(100), s => Assert.InRange(s, 16380, 16390));
        Assert.All(samples.Skip(100), s => Assert.Equal(0, s));
    }

    [Fact]
    public void A_source_that_is_silent_does_not_stop_the_other_from_being_heard()
    {
        short[] samples = Samples(new AudioMixer([new Constant(0.5f, available: 0), new Constant(0.25f)]).NextChunk());

        Assert.All(samples, s => Assert.InRange(s, 8185, 8195));
    }

    [Fact]
    public void Successive_chunks_read_on_from_where_the_sources_left_off()
    {
        var mixer = new AudioMixer([new Constant(0.5f, available: 1920 + 10)]); // a chunk and ten more samples

        short[] first = Samples(mixer.NextChunk());
        short[] second = Samples(mixer.NextChunk());

        Assert.All(first, s => Assert.NotEqual(0, s));
        Assert.All(second.Take(10), s => Assert.NotEqual(0, s));
        Assert.All(second.Skip(10), s => Assert.Equal(0, s));
    }

    [Fact]
    public void Each_source_has_its_own_level_that_follows_how_loud_it_is()
    {
        var mixer = new AudioMixer([new Constant(0.5f), new Constant(0f)]);

        mixer.NextChunk();

        Assert.Equal(0.5f, mixer.Levels[0], 3);
        Assert.Equal(0f, mixer.Levels[1]);
    }

    [Fact]
    public void A_level_falls_off_gradually_when_the_sound_stops_instead_of_dropping_at_once()
    {
        var mixer = new AudioMixer([new Constant(0.8f, available: 1920)]); // exactly one chunk, then silence

        mixer.NextChunk();
        float loud = mixer.Levels[0];
        mixer.NextChunk();
        float after = mixer.Levels[0];

        Assert.InRange(after, loud * 0.9f, loud * 0.95f);
        for (int i = 0; i < 100; i++)
        {
            mixer.NextChunk();
        }
        Assert.True(mixer.Levels[0] < 0.01f, "it should fade out to nothing");
    }

    [Fact]
    public void A_muted_source_is_not_heard_but_the_others_are()
    {
        var mixer = new AudioMixer([new Constant(0.5f), new Constant(0.25f)]);
        mixer.SetGain(0, 0f);

        short[] samples = Samples(mixer.NextChunk());

        Assert.All(samples, s => Assert.InRange(s, 8185, 8195)); // only the 0.25 one
    }

    [Fact]
    public void The_volume_scales_a_source_and_can_be_changed_while_mixing()
    {
        var mixer = new AudioMixer([new Constant(0.4f)]);

        mixer.SetGain(0, 0.5f);
        short[] half = Samples(mixer.NextChunk());
        mixer.SetGain(0, 2f);
        short[] doubled = Samples(mixer.NextChunk());

        Assert.All(half, s => Assert.InRange(s, 6550, 6560));     // 0.2
        Assert.All(doubled, s => Assert.InRange(s, 26210, 26220)); // 0.8
    }

    [Fact]
    public void The_volume_is_kept_between_nothing_and_twice_as_loud()
    {
        var mixer = new AudioMixer([new Constant(0.1f)]);

        mixer.SetGain(0, -3f);
        Assert.Equal(0f, mixer.Gains[0]);
        mixer.SetGain(0, 50f);

        Assert.Equal(AudioMixer.MaxGain, mixer.Gains[0]);
    }

    [Fact]
    public void The_level_shown_is_what_is_recorded_so_a_muted_source_shows_none()
    {
        var mixer = new AudioMixer([new Constant(0.8f)]);
        mixer.SetGain(0, 0f);

        mixer.NextChunk();

        Assert.Equal(0f, mixer.Levels[0]);
    }
}
