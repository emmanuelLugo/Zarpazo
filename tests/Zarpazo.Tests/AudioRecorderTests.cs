using System.IO;
using NAudio.Wave;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>Records a pulse from a made-up microphone at a known moment and finds where it lands in the MP4.</summary>
[Collection("Recording")]
public sealed class AudioRecorderTests : IDisposable
{
    private const int PulseAtMs = 600;
    private const int Width = 320;
    private const int Height = 180;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public AudioRecorderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // A microphone that sends nothing until the test says so.
    private sealed class FakeCapture : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording()
        {
        }

        public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());

        public void Dispose()
        {
        }

        /// <summary>A loud tone of the given length, delivered the way a device delivers its sound.</summary>
        public void Pulse(int milliseconds)
        {
            int frames = 48 * milliseconds;
            var bytes = new byte[frames * 2 * 4];
            for (int n = 0; n < frames; n++)
            {
                float value = (float)(Math.Sin(2 * Math.PI * 1000 * n / 48000.0) * 0.6);
                BitConverter.GetBytes(value).CopyTo(bytes, n * 8);
                BitConverter.GetBytes(value).CopyTo(bytes, n * 8 + 4);
            }
            DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
        }
    }

    // Records two seconds of picture and a pulse of sound heard PulseAtMs into it, and says where the sound is in the file.
    private async Task<double> PulseOnsetMsAsync(int offsetMs)
    {
        string path = Path.Combine(_folder, $"offset{offsetMs}.mp4");
        var writer = new Mp4Writer(path, Width, Height, fps: 30, withAudio: true);
        var clock = new RecordingClock();
        var capture = new FakeCapture();
        var audio = new AudioRecorder([AudioInput.FromCapture(capture, "Micrófono", isMicrophone: true)], writer, clock, offsetMs);

        writer.Start();
        clock.Start();
        audio.Start();

        bool pulsed = false;
        var picture = new byte[Width * Height * 4];
        for (int frame = 0; clock.ElapsedMilliseconds < 2200; frame++)
        {
            if (!pulsed && clock.ElapsedMilliseconds >= PulseAtMs)
            {
                capture.Pulse(120);
                pulsed = true;
            }
            writer.AddVideo((byte[])picture.Clone(), TimeSpan.FromMilliseconds(clock.ElapsedMilliseconds));
            await Task.Delay(33);
        }
        audio.Stop();
        await writer.FinishAsync();

        return await OnsetMsAsync(path);
    }

    private static async Task<double> OnsetMsAsync(string mp4)
    {
        string wav = Path.ChangeExtension(mp4, ".wav");
        StorageFile source = await StorageFile.GetFileFromPathAsync(mp4);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(mp4)!);
        StorageFile target = await folder.CreateFileAsync(Path.GetFileName(wav), CreationCollisionOption.ReplaceExisting);
        PrepareTranscodeResult prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(source, target, MediaEncodingProfile.CreateWav(AudioEncodingQuality.High));
        await prepared.TranscodeAsync();

        using var reader = new WaveFileReader(wav);
        var samples = reader.ToSampleProvider();
        var buffer = new float[samples.WaveFormat.SampleRate * samples.WaveFormat.Channels];
        long index = 0;
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++, index++)
            {
                if (Math.Abs(buffer[i]) > 0.1f)
                {
                    return index / samples.WaveFormat.Channels * 1000.0 / samples.WaveFormat.SampleRate;
                }
            }
        }
        return -1;
    }

    [Fact]
    public async Task Without_an_offset_the_sound_lands_where_it_was_heard()
    {
        double onset = await PulseOnsetMsAsync(0);

        Assert.InRange(onset, PulseAtMs - 80, PulseAtMs + 200);
    }

    [Fact]
    public async Task A_later_offset_moves_the_sound_later_by_that_much()
    {
        double without = await PulseOnsetMsAsync(0);
        double later = await PulseOnsetMsAsync(200);

        Assert.InRange(later - without, 130, 270);
    }

    [Fact]
    public async Task An_earlier_offset_moves_the_sound_earlier_by_that_much()
    {
        double without = await PulseOnsetMsAsync(0);
        double earlier = await PulseOnsetMsAsync(-200);

        Assert.InRange(without - earlier, 130, 270);
    }
}
