using System.Runtime.InteropServices;
using NAudio.MediaFoundation;

namespace Zarpazo.Recording;

/// <summary>
/// Reads the pictures of a video file one after the other, decoded to BGRA, with the decoder that comes with Windows (Media
/// Foundation, through the bindings NAudio already brings). The decoder hands over NV12 (brightness, then colour at half
/// size) and the colour conversion is done here, in parallel: Windows' own conversion to RGB takes about 75 ms a picture at
/// 1080p, several times longer than decoding.
/// </summary>
internal sealed class VideoFrameReader : IDisposable
{
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
    private const int AllStreams = unchecked((int)0xFFFFFFFE);

    private static readonly Guid Nv12 = new("3231564e-0000-0010-8000-00aa00389b71");
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid YuvMatrix = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    private static readonly Guid NominalRange = new("c21b8ee5-b956-4071-8daf-325edf5cab11");

    private readonly IMFSourceReader _reader;
    private int _frameHeight; // the decoder works in blocks of 16 rows, so 180 can come as 192
    private YuvConverter _converter = null!;
    private byte[] _nv12 = [];

    /// <param name="width">The size of the pictures wanted: the video's own size. Rows and columns the decoder adds beyond it are left out.</param>
    /// <exception cref="COMException">Windows cannot read the file.</exception>
    public VideoFrameReader(string path, int width, int height)
    {
        Width = width;
        Height = height;
        MediaFoundationApi.Startup();

        MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out _reader);
        _reader.SetStreamSelection(AllStreams, false);
        _reader.SetStreamSelection(FirstVideoStream, true);
        IMFMediaType wanted = MediaFoundationApi.CreateMediaType();
        wanted.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
        wanted.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, Nv12);
        _reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, wanted);
        Marshal.ReleaseComObject(wanted);

        ReadFormat();
    }

    // The size the decoder hands over and its colour formula. It can change when the first picture comes out (the padded
    // height is only known then), so it is read again whenever the reader says so.
    private void ReadFormat()
    {
        _reader.GetCurrentMediaType(FirstVideoStream, out IMFMediaType current);
        try
        {
            current.GetUINT64(FrameSize, out long size);
            _frameHeight = (int)(size & 0xFFFFFFFF);
            if ((int)(size >> 32) < Width || _frameHeight < Height)
            {
                throw new InvalidOperationException($"The video decodes at {size >> 32}x{_frameHeight}, smaller than {Width}x{Height}.");
            }

            // HD video uses the BT.709 colours and the rest BT.601, unless the file says otherwise; the levels go from 16 to
            // 235 unless it says they are full.
            bool hd = TryGet(current, YuvMatrix) is int matrix ? matrix == 1 : Height >= 720;
            bool full = TryGet(current, NominalRange) == 1;
            _converter = new YuvConverter(hd, full);
        }
        finally
        {
            Marshal.ReleaseComObject(current);
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Fills <paramref name="into"/> (Width x Height x 4 bytes, BGRA, top row first) with the next picture.</summary>
    /// <returns>False at the end of the video.</returns>
    public bool TryRead(byte[] into, out TimeSpan time)
    {
        while (true)
        {
            _reader.ReadSample(FirstVideoStream, 0, out _, out MF_SOURCE_READER_FLAG flags, out ulong timestamp, out IMFSample? sample);
            if (flags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
            {
                if (sample != null)
                {
                    Marshal.ReleaseComObject(sample);
                }
                time = default;
                return false;
            }
            if (flags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED))
            {
                ReadFormat();
            }
            if (sample == null)
            {
                continue; // a gap in the stream, not a picture
            }

            try
            {
                time = TimeSpan.FromTicks((long)timestamp); // both count in 100 ns
                Convert(sample, into);
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }
    }

    private void Convert(IMFSample sample, byte[] into)
    {
        sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        try
        {
            buffer.Lock(out IntPtr data, out _, out int length);
            try
            {
                // One byte of brightness per pixel over the whole (padded) height, then half as many rows of colour: the
                // length says how wide each row really is.
                int pitch = length / (_frameHeight * 3 / 2);
                if (_nv12.Length < length)
                {
                    _nv12 = new byte[length];
                }
                Marshal.Copy(data, _nv12, 0, length);
                _converter.ToBgra(_nv12, pitch, pitch * _frameHeight, into, Width, Height);
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

    /// <summary>
    /// The frame rate the video was recorded at: from the usual gap between its pictures, read without decoding them. Windows
    /// reports the average instead, which in a recording where some pictures came late is lower (28.75 for a 30 fps one).
    /// </summary>
    /// <returns>Null when the video has fewer than two pictures.</returns>
    public static double? RecordedFrameRate(string path)
    {
        MediaFoundationApi.Startup();
        MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out IMFSourceReader reader);
        try
        {
            reader.SetStreamSelection(AllStreams, false);
            reader.SetStreamSelection(FirstVideoStream, true);
            var times = new List<long>();
            while (true)
            {
                reader.ReadSample(FirstVideoStream, 0, out _, out MF_SOURCE_READER_FLAG flags, out ulong timestamp, out IMFSample? sample);
                if (sample != null)
                {
                    times.Add((long)timestamp);
                    Marshal.ReleaseComObject(sample);
                }
                if (flags.HasFlag(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
                {
                    break;
                }
            }

            times.Sort(); // stored in decoding order, which is not always the order they are shown in
            List<long> gaps = times.Zip(times.Skip(1), (a, b) => b - a).Where(gap => gap > 0).Order().ToList();
            return gaps.Count == 0 ? null : TimeSpan.TicksPerSecond / (double)gaps[gaps.Count / 2];
        }
        finally
        {
            Marshal.ReleaseComObject(reader);
        }
    }

    private static int? TryGet(IMFMediaType type, Guid key)
    {
        try
        {
            type.GetUINT32(key, out int value);
            return value;
        }
        catch (COMException)
        {
            return null; // not set
        }
    }

    public void Dispose() => Marshal.ReleaseComObject(_reader);
}
