using NAudio.Wave;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class VoiceLevelerTests
{
    // Something like speech, a tone in bursts with short gaps as between syllables, at one loudness after another:
    // (peak, seconds) in turn. A peak marked as noise is steady, like the hiss of a room.
    private sealed class Tone(params (float Peak, double Seconds)[] parts) : ISampleProvider
    {
        public const float NoisePeak = 0.001f;

        private long _frame;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i += 2, _frame++)
            {
                double second = _frame / 48000.0;
                float peak = 0;
                foreach ((float partPeak, double seconds) in parts)
                {
                    if (second < seconds)
                    {
                        peak = partPeak;
                        break;
                    }
                    second -= seconds;
                }
                bool gap = peak > NoisePeak && _frame % 9600 >= 7680; // 160 ms of sound, 40 ms of nothing
                float value = gap ? 0 : (float)(Math.Sin(2 * Math.PI * 440 * _frame / 48000.0) * peak);
                buffer[offset + i] = value;
                buffer[offset + i + 1] = value;
            }
            return count;
        }
    }

    // What comes out, second by second, as the loudness (RMS) of each second.
    private static float[] LoudnessPerSecond(ISampleProvider source, int seconds)
    {
        var leveler = new VoiceLeveler(source);
        var chunk = new float[1920]; // the 20 ms the mixer asks for
        var result = new float[seconds];
        for (int s = 0; s < seconds; s++)
        {
            double squares = 0;
            for (int c = 0; c < 50; c++)
            {
                Assert.Equal(chunk.Length, leveler.Read(chunk, 0, chunk.Length));
                squares += chunk.Sum(v => (double)v * v);
            }
            result[s] = (float)Math.Sqrt(squares / (chunk.Length * 50));
        }
        return result;
    }

    private const float Spoken = 0.179f; // the loudness of a second of bursts that reach the target: a fifth of it is gaps

    [Fact]
    public void A_quiet_voice_is_brought_up_to_a_speaking_volume()
    {
        float[] loudness = LoudnessPerSecond(new Tone((0.02f, 10)), seconds: 4); // 20 dB too quiet, like a phone used as a microphone

        Assert.InRange(loudness[3], Spoken * 0.85f, Spoken * 1.15f);
    }

    [Fact]
    public void A_voice_that_is_too_loud_is_brought_down()
    {
        float[] loudness = LoudnessPerSecond(new Tone((0.9f, 10)), seconds: 4);

        Assert.InRange(loudness[3], Spoken * 0.85f, Spoken * 1.15f);
    }

    [Fact]
    public void Background_noise_before_anyone_speaks_does_not_drive_the_volume_up()
    {
        // Otherwise the first word would come out at full blast.
        float[] loudness = LoudnessPerSecond(new Tone((Tone.NoisePeak, 10)), seconds: 5);

        float boost = loudness[4] / (Tone.NoisePeak / MathF.Sqrt(2));
        Assert.InRange(boost, 3.5f, 4.5f); // the boost it starts with
    }

    [Fact]
    public void The_noise_in_a_pause_is_not_pumped_up_beyond_what_the_voice_got()
    {
        // Voice, then three seconds of faint noise: the noise gets the same boost the voice had, not the maximum.
        float[] loudness = LoudnessPerSecond(new Tone((0.02f, 3), (Tone.NoisePeak, 3)), seconds: 6);

        float boost = loudness[5] / (Tone.NoisePeak / MathF.Sqrt(2));
        Assert.InRange(boost, 10f, 18f); // the voice needed about 14 times
    }

    [Fact]
    public void The_first_word_does_not_come_out_louder_than_the_rest()
    {
        // A second of room noise, then a quiet voice: the start of the voice must not be above where it settles.
        var leveler = new VoiceLeveler(new Tone((Tone.NoisePeak, 1), (0.02f, 5)));
        var chunk = new float[1920];
        var peaks = new List<float>();
        for (int c = 0; c < 300; c++)
        {
            leveler.Read(chunk, 0, chunk.Length);
            peaks.Add(chunk.Max(Math.Abs));
        }

        float start = peaks.Skip(50).Take(25).Max(); // the first half second of voice
        float settled = peaks.Skip(250).Max();
        Assert.True(start <= settled * 1.1f, $"start {start}, settled {settled}");
    }

    [Fact]
    public void The_rumble_under_the_voice_is_taken_out()
    {
        var leveler = new VoiceLeveler(new Hum());
        var chunk = new float[1920];
        for (int c = 0; c < 50; c++)
        {
            leveler.Read(chunk, 0, chunk.Length);
        }

        Assert.InRange(chunk.Max(Math.Abs), 0f, 0.02f); // 0.05 came in, and it starts with a boost of 4
    }

    // A 30 Hz hum, like a desk being bumped or a fan.
    private sealed class Hum : ISampleProvider
    {
        private long _frame;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i += 2, _frame++)
            {
                buffer[offset + i] = buffer[offset + i + 1] = (float)(Math.Sin(2 * Math.PI * 30 * _frame / 48000.0) * 0.05);
            }
            return count;
        }
    }

    [Fact]
    public void A_sudden_shout_never_goes_past_full_scale()
    {
        var leveler = new VoiceLeveler(new Tone((0.02f, 2), (1f, 1)));
        var chunk = new float[1920];
        float peak = 0;
        for (int c = 0; c < 150; c++)
        {
            leveler.Read(chunk, 0, chunk.Length);
            peak = Math.Max(peak, chunk.Max(Math.Abs));
        }

        Assert.InRange(peak, 0.5f, 0.9f);
    }

    [Fact]
    public void A_device_that_has_nothing_to_give_gives_nothing()
    {
        Assert.Equal(0, new VoiceLeveler(new Silent()).Read(new float[1920], 0, 1920));
    }

    private sealed class Silent : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count) => 0;
    }
}
