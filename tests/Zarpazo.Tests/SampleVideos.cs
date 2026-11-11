using System.IO;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>
/// Two three-second videos made once for a whole test class (they are written in real time, so making one per test is slow):
/// the picture gets brighter every frame, so which part is where can be told from a single frame.
/// </summary>
public sealed class SampleVideos : IAsyncLifetime
{
    public const int Frames = 90;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", "samples-" + Guid.NewGuid().ToString("N"));

    public string Silent { get; private set; } = "";

    public string WithSound { get; private set; } = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        Silent = await MakeAsync("silent.mp4", withSound: false);
        WithSound = await MakeAsync("with-sound.mp4", withSound: true);
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
        return Task.CompletedTask;
    }

    private async Task<string> MakeAsync(string name, bool withSound)
    {
        string path = Path.Combine(_folder, name);
        var writer = new Mp4Writer(path, 320, 180, fps: 30, withAudio: withSound);
        writer.Start();
        for (int frame = 0; frame < Frames; frame++)
        {
            var pixels = new byte[320 * 180 * 4];
            Array.Fill(pixels, (byte)(frame * 2));
            writer.AddVideo(pixels, TimeSpan.FromSeconds(frame / 30.0));
            if (withSound && frame % 3 == 2)
            {
                var pcm = new byte[Mp4Writer.AudioSampleRate * 100 / 1000 * 4];
                for (int i = 0; i < pcm.Length; i += 2)
                {
                    BitConverter.GetBytes((short)(Math.Sin(i * 0.05) * 6000)).CopyTo(pcm, i);
                }
                writer.AddAudio(pcm, TimeSpan.FromSeconds((frame - 2) / 30.0));
            }
            await Task.Delay(33); // real time: written faster, the queue would drop pictures and shorten the video
        }
        await writer.FinishAsync();
        return path;
    }
}
