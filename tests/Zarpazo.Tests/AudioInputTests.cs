using NAudio.Wave;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class AudioInputTests
{
    // A device that counts how often it was asked to start.
    private sealed class CountingCapture : IWaveIn
    {
        public int Starts { get; private set; }

        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording() => Starts++;

        public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());

        public void Dispose() => DataAvailable = null;
    }

    [Fact]
    public void A_device_tried_before_the_recording_is_not_started_again_when_it_begins()
    {
        // The video job tries every device while preparing, so one Windows won't open is left out before the file is made;
        // the recorder starts them all again when the recording begins.
        var capture = new CountingCapture();
        using AudioInput input = AudioInput.FromCapture(capture, "Micrófono", isMicrophone: true);

        input.Start();
        input.Start();

        Assert.Equal(1, capture.Starts);
    }
}
