using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Zarpazo.Recording;

/// <summary>
/// Writes an MP4 (H.264 video, AAC audio) while the pictures and sound are still being captured, using the encoder that
/// comes with Windows. The encoder asks for the next sample whenever it is ready; what has been captured waits in a queue.
/// </summary>
internal sealed class Mp4Writer
{
    public const int AudioSampleRate = 48000;
    public const int AudioChannels = 2;

    private const int VideoQueueSize = 12; // a third of a second; when the encoder falls further behind, frames are dropped rather than memory growing
    private const uint MinBitrate = 1_500_000;
    private const uint MaxBitrate = 24_000_000;
    private const uint AudioBitrate = 128_000;
    private static readonly Guid MfDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

    private readonly string _path;
    private readonly int _fps;
    private readonly bool _withAudio;
    private readonly Channel<MediaStreamSample> _video;
    private readonly Channel<MediaStreamSample>? _audio;
    private readonly MediaStreamSource _source;
    private readonly MediaEncodingProfile _profile;
    private readonly ConcurrentBag<byte[]> _spareBuffers = new(); // pictures are 8 MB at 1080p: new arrays every frame would fill the large object heap
    private readonly int _pictureBytes;
    private volatile bool _finished;
    private Task? _encoding;

    /// <param name="width">Both sizes must be even: H.264 stores the picture in blocks of two pixels.</param>
    /// <param name="bitsPerPixelPerFrame">How much data each pixel of each picture gets, which sets the bitrate: more is sharper and bigger.</param>
    public Mp4Writer(string path, int width, int height, int fps, bool withAudio, double bitsPerPixelPerFrame = 0.12)
    {
        if (width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentException("The picture size must be even.");
        }

        _path = path;
        _fps = fps;
        _pictureBytes = width * height * 4;
        _withAudio = withAudio;
        _video = Channel.CreateBounded<MediaStreamSample>(new BoundedChannelOptions(VideoQueueSize)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then reports "full" and the caller drops the frame
        });

        VideoEncodingProperties videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        videoProperties.FrameRate.Numerator = (uint)fps;
        videoProperties.FrameRate.Denominator = 1;
        // Media Foundation reads uncompressed RGB as bottom-up unless the stride says otherwise, which turned the video upside
        // down. A positive stride (MF_MT_DEFAULT_STRIDE) declares the rows as they are: top row first.
        videoProperties.Properties[MfDefaultStride] = (uint)(width * 4);
        _source =new MediaStreamSource(new VideoStreamDescriptor(videoProperties)) { BufferTime = TimeSpan.Zero };

        if (withAudio)
        {
            _audio = Channel.CreateUnbounded<MediaStreamSample>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            _source.AddStreamDescriptor(new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(AudioSampleRate, AudioChannels, 16)));
        }
        _source.SampleRequested += OnSampleRequested;

        _profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        _profile.Video.Width = (uint)width;
        _profile.Video.Height = (uint)height;
        _profile.Video.FrameRate.Numerator = (uint)fps;
        _profile.Video.FrameRate.Denominator = 1;
        _profile.Video.Bitrate = (uint)Math.Clamp(width * height * fps * bitsPerPixelPerFrame, MinBitrate, MaxBitrate);
        _profile.Audio = withAudio ? AudioEncodingProperties.CreateAac(AudioSampleRate, AudioChannels, AudioBitrate) : null;
    }

    /// <summary>Starts encoding. Pictures and sound added from here on are written as they come.</summary>
    public void Start() => _encoding = EncodeAsync();

    /// <summary>An array for a picture: a recycled one when the encoder has finished with it. Hand it back through <see cref="AddVideo"/>.</summary>
    public byte[] RentPicture() => _spareBuffers.TryTake(out byte[]? spare) ? spare : new byte[_pictureBytes];

    /// <param name="bgra">The picture, tightly packed, from <see cref="RentPicture"/>; the writer owns the array from here on.</param>
    /// <param name="time">When it was taken, from the start of the recording.</param>
    /// <returns>False when the encoder is too far behind and the picture was dropped.</returns>
    public bool AddVideo(byte[] bgra, TimeSpan time)
    {
        MediaStreamSample sample = MediaStreamSample.CreateFromBuffer(bgra.AsBuffer(), time);
        sample.Duration = TimeSpan.FromSeconds(1.0 / _fps);
        sample.Processed += (_, _) => Recycle(bgra);
        if (_video.Writer.TryWrite(sample))
        {
            return true;
        }
        Recycle(bgra);
        return false;
    }

    private void Recycle(byte[] picture)
    {
        if (!_finished && picture.Length == _pictureBytes)
        {
            _spareBuffers.Add(picture);
        }
    }

    /// <param name="pcm">16-bit stereo sound at 48 kHz; the writer keeps the array.</param>
    public void AddAudio(byte[] pcm, TimeSpan time)
    {
        if (_audio == null)
        {
            return;
        }

        MediaStreamSample sample = MediaStreamSample.CreateFromBuffer(pcm.AsBuffer(), time);
        sample.Duration = TimeSpan.FromSeconds(pcm.Length / (double)(AudioChannels * 2 * AudioSampleRate));
        _audio.Writer.TryWrite(sample);
    }

    /// <summary>Ends the streams and waits until the file is complete.</summary>
    public async Task FinishAsync()
    {
        _video.Writer.TryComplete();
        _audio?.Writer.TryComplete();
        try
        {
            if (_encoding != null)
            {
                await _encoding;
            }
        }
        finally
        {
            _finished = true;
            _spareBuffers.Clear();
        }
    }

    private async Task EncodeAsync()
    {
        await using FileStream file = new(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true }; // the software encoder ignores the bitrate asked for
        PrepareTranscodeResult prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(_source, file.AsRandomAccessStream(), _profile);
        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException($"Windows no puede codificar el video ({prepared.FailureReason}).");
        }
        await prepared.TranscodeAsync();
    }

    // The encoder asks for a sample of one stream; the answer waits here until one has been captured, or says "no more".
    private async void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        MediaStreamSourceSampleRequest request = args.Request;
        MediaStreamSourceSampleRequestDeferral deferral = request.GetDeferral();
        try
        {
            ChannelReader<MediaStreamSample> reader = request.StreamDescriptor is VideoStreamDescriptor ? _video.Reader : _audio!.Reader;
            // Null sample = end of stream, which is what lets the encoder finish and close the file.
            request.Sample = await reader.WaitToReadAsync() && reader.TryRead(out MediaStreamSample? sample) ? sample : null;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
