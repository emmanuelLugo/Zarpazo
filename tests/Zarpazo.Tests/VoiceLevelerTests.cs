using NAudio.Wave;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class VoiceLevelerTests
{
    // A steady tone at one loudness after another: (peak, seconds) in turn.
    private sealed class Tone(params (float Peak, double Seconds)[] parts) : ISampleProvider
    {
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
                float value = (float)(Math.Sin(2 * Math.PI * 440 * _frame / 48000.0) * peak);
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

    [Fact]
    public void A_quiet_voice_is_brought_up_to_a_speaking_volume()
    {
        float[] loudness = LoudnessPerSecond(new Tone((0.02f, 10)), seconds: 3); // 20 dB too quiet, like a phone used as a microphone

        Assert.InRange(loudness[2], VoiceLeveler.TargetLevel * 0.9f, VoiceLeveler.TargetLevel * 1.1f);
    }

    [Fact]
    public void A_voice_that_is_too_loud_is_brought_down()
    {
        float[] loudness = LoudnessPerSecond(new Tone((0.9f, 10)), seconds: 3);

        Assert.InRange(loudness[2], VoiceLeveler.TargetLevel * 0.9f, VoiceLeveler.TargetLevel * 1.1f);
    }

    [Fact]
    public void Background_noise_alone_is_left_as_it_is()
    {
        float[] loudness = LoudnessPerSecond(new Tone((0.001f, 10)), seconds: 3);

        Assert.InRange(loudness[2], 0.0006f, 0.0008f); // the RMS of a tone that peaks at 0.001
    }

    [Fact]
    public void The_noise_in_a_pause_is_not_pumped_up_beyond_what_the_voice_got()
    {
        // Voice, then three seconds of faint noise: the noise gets the same boost the voice had, not the maximum.
        float[] loudness = LoudnessPerSecond(new Tone((0.02f, 2), (0.001f, 3)), seconds: 5);

        float boost = loudness[4] / (0.001f / MathF.Sqrt(2));
        Assert.InRange(boost, 10f, 18f); // the voice needed about 14 times
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
