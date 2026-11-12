using System.Runtime.InteropServices;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Zarpazo.Recording;

/// <summary>Reads the sound of a video file in the pieces it is stored in, decoded to 16-bit stereo at 48 kHz (what <see cref="Mp4Writer"/> takes).</summary>
internal sealed class AudioChunkReader : IDisposable
{
    private const int FirstAudioStream = unchecked((int)0xFFFFFFFD);
    private const int AllStreams = unchecked((int)0xFFFFFFFE);

    private readonly IMFSourceReader _reader;

    /// <exception cref="COMException">Windows cannot read the file, or it has no sound.</exception>
    public AudioChunkReader(string path)
    {
        MediaFoundationApi.Startup();
        MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out _reader);
        _reader.SetStreamSelection(AllStreams, false);
        _reader.SetStreamSelection(FirstAudioStream, true);
        IMFMediaType wanted = MediaFoundationApi.CreateMediaTypeFromWaveFormat(new WaveFormat(Mp4Writer.AudioSampleRate, 16, Mp4Writer.AudioChannels));
        _reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, wanted);
        Marshal.ReleaseComObject(wanted);
    }

    /// <summary>The next piece of sound and when it starts; null at the end.</summary>
    public byte[]? Read(out TimeSpan time)
    {
        while (true)
        {
            _reader.ReadSample(FirstAudioStream, 0, out _, out MF_SOURCE_READER_FLAG flags, out ulong timestamp, out IMFSample? sample);
            if (flags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
            {
                if (sample != null)
                {
                    Marshal.ReleaseComObject(sample);
                }
                time = default;
                return null;
            }
            if (sample == null)
            {
                continue;
            }

            try
            {
                time = TimeSpan.FromTicks((long)timestamp);
                sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
                try
                {
                    buffer.Lock(out IntPtr data, out _, out int length);
                    try
                    {
                        var pcm = new byte[length];
                        Marshal.Copy(data, pcm, 0, length);
                        return pcm;
                    }
                    finally
                    {
                        buffer.Unlock();
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(buffer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }
    }

    public void Dispose() => Marshal.ReleaseComObject(_reader);
}
