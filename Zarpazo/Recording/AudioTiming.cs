namespace Zarpazo.Recording;

/// <summary>Moves the sound against the picture by a fixed amount, for when the two come out of step.</summary>
internal static class AudioTiming
{
    private const int BytesPerMillisecond = Mp4Writer.AudioSampleRate / 1000 * Mp4Writer.AudioChannels * 2;

    /// <summary>
    /// Where a chunk goes once the offset is applied. A chunk that would start before the beginning of the recording loses its first
    /// part instead of starting at a negative time; one that ends before it is dropped (null).
    /// </summary>
    public static (byte[] Pcm, long TimeMs)? Shift(byte[] pcm, long startMs, int offsetMs)
    {
        long time = startMs + offsetMs;
        long lengthMs = pcm.Length / BytesPerMillisecond;
        if (time + lengthMs <= 0)
        {
            return null;
        }
        if (time >= 0)
        {
            return (pcm, time);
        }

        int cut = (int)(-time) * BytesPerMillisecond;
        return (pcm[cut..], 0);
    }

    /// <summary>Silence of the given length, to fill the start of the sound when it is moved later.</summary>
    public static byte[] Silence(int milliseconds) => new byte[milliseconds * BytesPerMillisecond];
}
