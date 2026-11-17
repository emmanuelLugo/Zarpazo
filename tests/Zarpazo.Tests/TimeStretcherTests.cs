using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class TimeStretcherTests
{
    private const int Rate = Mp4Writer.AudioSampleRate;

    // A steady tone of the given pitch, 16-bit stereo.
    private static byte[] Tone(double hertz, double seconds)
    {
        int frames = (int)(seconds * Rate);
        var pcm = new byte[frames * 4];
        for (int i = 0; i < frames; i++)
        {
            short value = (short)(Math.Sin(2 * Math.PI * hertz * i / Rate) * 8000);
            BitConverter.GetBytes(value).CopyTo(pcm, i * 4);
            BitConverter.GetBytes(value).CopyTo(pcm, i * 4 + 2);
        }
        return pcm;
    }

    // Feeds the sound in pieces of the size an AAC decoder gives, and returns everything that comes out.
    private static byte[] Stretch(byte[] input, double speed, int piece = 1024 * 4)
    {
        var stretcher = new TimeStretcher(speed);
        var output = new List<byte>();
        for (int at = 0; at < input.Length; at += piece)
        {
            output.AddRange(stretcher.Process(input[at..Math.Min(input.Length, at + piece)]));
        }
        output.AddRange(stretcher.Flush());
        return output.ToArray();
    }

    // The pitch, from how often the left channel crosses zero going up.
    private static double Hertz(byte[] pcm, int fromFrame, int toFrame)
    {
        int crossings = 0;
        short previous = BitConverter.ToInt16(pcm, fromFrame * 4);
        for (int i = fromFrame + 1; i < toFrame; i++)
        {
            short value = BitConverter.ToInt16(pcm, i * 4);
            if (previous < 0 && value >= 0)
            {
                crossings++;
            }
            previous = value;
        }
        return crossings * (double)Rate / (toFrame - fromFrame);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(0.5)]
    [InlineData(4)]
    public void The_sound_lasts_the_input_divided_by_the_speed(double speed)
    {
        byte[] output = Stretch(Tone(440, 3), speed);

        Assert.Equal(TimeStretcher.OutputLength(3 * Rate, speed) * 4, output.Length);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(0.5)]
    public void The_pitch_does_not_change(double speed)
    {
        byte[] output = Stretch(Tone(440, 3), speed);
        int frames = output.Length / 4;

        // Away from both ends, where only whole windows play.
        Assert.InRange(Hertz(output, frames / 4, frames * 3 / 4), 430, 450);
    }

    [Fact]
    public void The_pieces_the_sound_arrives_in_do_not_change_the_result()
    {
        byte[] input = Tone(300, 2);

        byte[] small = Stretch(input, 1.5, piece: 1000 * 4);
        byte[] whole = Stretch(input, 1.5, piece: input.Length);

        Assert.Equal(whole.Length, small.Length);
        Assert.Equal(whole, small);
    }

    [Fact]
    public void Quiet_stays_quiet_and_a_tone_does_not_get_louder()
    {
        byte[] quiet = Stretch(new byte[Rate * 4], 2);
        byte[] output = Stretch(Tone(440, 2), 1.5);

        Assert.All(quiet, value => Assert.Equal(0, value));
        int peak = 0;
        for (int i = 0; i < output.Length / 4; i++)
        {
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(output, i * 4)));
        }
        Assert.InRange(peak, 7000, 9000); // the tone's own 8000: the overlapping windows add up to one
    }

    [Fact]
    public void A_very_short_sound_and_an_empty_one_come_out_whole()
    {
        Assert.Empty(Stretch([], 2));
        Assert.Equal(TimeStretcher.OutputLength(100, 2) * 4, Stretch(new byte[100 * 4], 2).Length);
    }
}
