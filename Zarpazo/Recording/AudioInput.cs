using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Zarpazo.Recording;

/// <summary>
/// One source of sound, the microphone or what Windows is playing, brought to 48 kHz stereo whatever the device uses.
/// What the device has sent so far waits in a buffer until the mixer asks for it.
/// </summary>
internal sealed class AudioInput : IDisposable
{
    private const int MaxBufferedMilliseconds = 120; // beyond this the sound would lag behind the picture, so the oldest goes

    private readonly IWaveIn _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly byte[] _discard;
    private bool _started;

    private AudioInput(IWaveIn capture, string name, bool isMicrophone)
    {
        _capture = capture;
        Name = name;
        IsMicrophone = isMicrophone;

        WaveFormat format = capture.WaveFormat;
        _buffer = new BufferedWaveProvider(format)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
            ReadFully = false, // nothing buffered means nothing returned; the mixer fills the gap with silence
        };
        _discard = new byte[format.AverageBytesPerSecond]; // the most that can ever be discarded at once

        ISampleProvider samples = _buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1)
        {
            samples = new MonoToStereoSampleProvider(samples);
        }
        else if (samples.WaveFormat.Channels > 2)
        {
            // Surround: the front left and right are what the speakers of most people play.
            var stereo = new MultiplexingSampleProvider([samples], 2);
            stereo.ConnectInputToOutput(0, 0);
            stereo.ConnectInputToOutput(1, 1);
            samples = stereo;
        }
        if (samples.WaveFormat.SampleRate != Mp4Writer.AudioSampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, Mp4Writer.AudioSampleRate);
        }
        Samples = samples;

        _capture.DataAvailable += (_, e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        // If the device goes away mid-recording the input just stops sending, which the mixer turns into silence.
    }

    /// <summary>What the person sees in the bar: "Micrófono" or "Sonido de Windows".</summary>
    public string Name { get; }

    public bool IsMicrophone { get; }

    /// <summary>The sound as 48 kHz stereo floats.</summary>
    public ISampleProvider Samples { get; }

    /// <summary>An input fed by any source of sound; the tests use it to play a known sound without any hardware.</summary>
    internal static AudioInput FromCapture(IWaveIn capture, string name, bool isMicrophone) => new(capture, name, isMicrophone);

    /// <param name="deviceId">The microphone to use, or empty for the one Windows uses by default.</param>
    /// <exception cref="InvalidOperationException">There is no such microphone.</exception>
    public static AudioInput Microphone(string deviceId = "")
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice device = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                : enumerator.GetDevice(deviceId);
            return new AudioInput(new WasapiCapture(device), "Micrófono", isMicrophone: true);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(deviceId)
                ? "No se encontró un micrófono."
                : "El micrófono elegido no está disponible. Elige otro en el panel de grabación.");
        }
    }

    /// <param name="deviceId">The speakers or headphones to take the sound from, or empty for the ones Windows uses by default.</param>
    /// <exception cref="InvalidOperationException">There is no such output device.</exception>
    public static AudioInput SystemSound(string deviceId = "")
    {
        try
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return new AudioInput(new WasapiLoopbackCapture(), "Sonido de Windows", isMicrophone: false);
            }

            using var enumerator = new MMDeviceEnumerator();
            return new AudioInput(new WasapiLoopbackCapture(enumerator.GetDevice(deviceId)), "Sonido de Windows", isMicrophone: false);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(deviceId)
                ? "No se encontró un dispositivo de salida de sonido."
                : "El dispositivo de sonido elegido no está disponible. Elige otro en el panel de grabación.");
        }
    }

    /// <summary>Starts listening; a second call does nothing.</summary>
    /// <exception cref="System.Runtime.InteropServices.COMException">Windows would not open the device, such as the
    /// speakers of a Bluetooth headset in hands-free mode, whose sound can't be captured.</exception>
    public void Start()
    {
        if (!_started)
        {
            _capture.StartRecording();
            _started = true;
        }
    }

    /// <summary>Throws away what the device has sent and nobody has used yet, such as what came in during a pause.</summary>
    public void Clear() => _buffer.ClearBuffer();

    /// <summary>Drops the oldest sound if the device is delivering faster than it is being used, so the delay can't build up.</summary>
    public void TrimBacklog()
    {
        double bytesPerMs = _capture.WaveFormat.AverageBytesPerSecond / 1000.0;
        int excess = _buffer.BufferedBytes - (int)(MaxBufferedMilliseconds * bytesPerMs);
        if (excess > 0)
        {
            excess -= excess % _capture.WaveFormat.BlockAlign; // whole frames only, or the channels would swap
            _buffer.Read(_discard, 0, Math.Min(excess, _discard.Length));
        }
    }

    public void Dispose()
    {
        try
        {
            _capture.StopRecording();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The device is already gone.
        }
        _capture.Dispose();
    }
}
