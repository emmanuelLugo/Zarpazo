using NAudio.Wave;

namespace Zarpazo.Recording;

/// <summary>Adds several sound sources together, one short chunk at a time, as the 16-bit stereo the video file takes.</summary>
internal sealed class AudioMixer(IReadOnlyList<ISampleProvider> sources)
{
    public const int ChunkMilliseconds = 20;
    public const int FramesPerChunk = Mp4Writer.AudioSampleRate * ChunkMilliseconds / 1000;

    private const int SamplesPerChunk = FramesPerChunk * Mp4Writer.AudioChannels;
    public const float MaxGain = 2f;
    private const float LevelDecay = 0.92f; // per chunk: about a third of the peak is left after a quarter of a second
    private readonly float[] _mix = new float[SamplesPerChunk];
    private readonly float[] _read = new float[SamplesPerChunk];
    private readonly float[] _levels = new float[sources.Count];
    private readonly float[] _gains = Enumerable.Repeat(1f, sources.Count).ToArray();

    /// <summary>The volume of each source: 0 mutes it, 1 leaves it as it comes. Can be changed while recording.</summary>
    public IReadOnlyList<float> Gains => _gains;

    public void SetGain(int source, float gain) => _gains[source] = Math.Clamp(gain, 0f, MaxGain);

    /// <summary>How loud each source is right now (0 to 1, in the order given), falling off gradually so that it can be read by eye.</summary>
    public IReadOnlyList<float> Levels => _levels;

    /// <summary>
    /// The next chunk. A source that has less than a chunk ready (a device that is silent sends nothing at all) counts as
    /// silence for the rest, so the sound never falls behind the picture.
    /// </summary>
    public byte[] NextChunk()
    {
        Array.Clear(_mix);
        for (int s = 0; s < sources.Count; s++)
        {
            Array.Clear(_read);
            int count = sources[s].Read(_read, 0, SamplesPerChunk);
            float peak = 0;
            float gain = _gains[s];
            for (int i = 0; i < count; i++)
            {
                float value = _read[i] * gain;
                _mix[i] += value;
                peak = Math.Max(peak, Math.Abs(value));
            }
            _levels[s] = Math.Max(peak, _levels[s] * LevelDecay);
        }

        var pcm = new byte[SamplesPerChunk * 2];
        for (int i = 0; i < SamplesPerChunk; i++)
        {
            // Two loud sources can add past full scale; cut there instead of wrapping around into noise.
            short value = (short)(Math.Clamp(_mix[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }
        return pcm;
    }
}
