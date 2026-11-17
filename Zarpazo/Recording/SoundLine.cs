namespace Zarpazo.Recording;

/// <summary>
/// The sound written to an <see cref="Mp4Writer"/> in order, on the clock of the video being made: where it ends so far, quiet
/// filled in where a piece has no sound, and pieces that start a hair early trimmed so that none overlaps the one before.
/// </summary>
internal sealed class SoundLine
{
    private const double Chunk = 0.02;
    private const int BytesPerFrame = Mp4Writer.AudioChannels * 2;

    private readonly Mp4Writer _writer;
    private readonly CancellationToken _cancel;

    public SoundLine(Mp4Writer writer, CancellationToken cancel)
    {
        _writer = writer;
        _cancel = cancel;
    }

    /// <summary>Where the sound written so far ends, in seconds.</summary>
    public double Written { get; private set; }

    /// <summary>Writes <paramref name="pcm"/> (16-bit stereo, 48 kHz) so that it starts at <paramref name="time"/>, with quiet before it if there is a gap.</summary>
    public async Task AddAsync(byte[] pcm, double time)
    {
        if (pcm.Length < BytesPerFrame)
        {
            return;
        }

        if (time - Written > 0.001)
        {
            await FillToAsync(time);
        }
        else if (time < Written - 0.0005)
        {
            // It starts before the previous sound ended: the part that overlaps is left out.
            int drop = (int)Math.Round((Written - time) * Mp4Writer.AudioSampleRate) * BytesPerFrame;
            if (drop >= pcm.Length)
            {
                return;
            }
            pcm = pcm[drop..];
            time = Written;
        }

        await _writer.AddAudioAsync(pcm, TimeSpan.FromSeconds(time), _cancel);
        Written = Math.Max(Written, time + pcm.Length / (double)(BytesPerFrame * Mp4Writer.AudioSampleRate));
    }

    /// <summary>Fills the sound from where it ends to <paramref name="time"/> with quiet, so a piece without sound is quiet for as long as it plays.</summary>
    public async Task FillToAsync(double time)
    {
        double from = Written;
        while (time - from > 0.001)
        {
            int frames = (int)Math.Round(Math.Min(Chunk, time - from) * Mp4Writer.AudioSampleRate);
            if (frames <= 0)
            {
                break;
            }
            await _writer.AddAudioAsync(new byte[frames * BytesPerFrame], TimeSpan.FromSeconds(from), _cancel);
            from += frames / (double)Mp4Writer.AudioSampleRate;
        }
        Written = Math.Max(from, time);
    }
}
