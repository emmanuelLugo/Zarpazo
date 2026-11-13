namespace Zarpazo.Recording;

/// <summary>
/// Keeps sound under a ceiling by turning it down for a moment when it goes over, instead of cutting the tops off the
/// wave (which is heard as crackle). Both channels are turned down together so the sound doesn't shift to one side.
/// </summary>
internal sealed class PeakLimiter(float ceiling = 0.89f, int channels = Mp4Writer.AudioChannels, int sampleRate = Mp4Writer.AudioSampleRate)
{
    private const double ReleaseSeconds = 0.12; // how long it takes to come back up after a loud moment

    private readonly float _release = (float)(1 - Math.Exp(-1 / (ReleaseSeconds * sampleRate)));
    private float _gain = 1f;

    /// <summary>Limits the first <paramref name="count"/> samples of interleaved sound, in place.</summary>
    public void Process(float[] buffer, int offset, int count)
    {
        for (int i = offset; i + channels <= offset + count; i += channels)
        {
            float peak = 0;
            for (int c = 0; c < channels; c++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[i + c]));
            }

            _gain += (1f - _gain) * _release;
            if (peak * _gain > ceiling)
            {
                _gain = ceiling / peak; // at once: nothing gets past the ceiling
            }
            for (int c = 0; c < channels; c++)
            {
                buffer[i + c] *= _gain;
            }
        }
    }
}
