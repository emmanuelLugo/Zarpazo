namespace Zarpazo.Recording;

/// <summary>
/// Listens to one source of sound without recording it, so the person can check that the microphone (or the sound of
/// Windows) is picking something up before starting. Nothing is stored; the sound is read only to measure it.
/// </summary>
internal sealed class AudioLevelProbe : IDisposable
{
    private const int PollMilliseconds = 50;
    private const float PollDecay = 0.8f; // the recording bar's fall-off (0.92 per 20 ms), at the pace of the polling

    private readonly AudioInput _input;
    private readonly float[] _read = new float[Mp4Writer.AudioSampleRate * Mp4Writer.AudioChannels * PollMilliseconds / 1000];
    private float _level;

    private AudioLevelProbe(AudioInput input) => _input = input;

    /// <summary>The source, or null when it can't be opened (no such device, or another program has it to itself).</summary>
    public static AudioLevelProbe? TryOpen(bool microphone, string deviceId)
    {
        AudioInput? input = null;
        try
        {
            input = microphone ? AudioInput.Microphone(deviceId) : AudioInput.SystemSound(deviceId);
            input.Start();
            return new AudioLevelProbe(input);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            input?.Dispose();
            return null;
        }
    }

    /// <summary>How loud it has been since the last call (0 to 1), falling off gradually so that it can be read by eye.</summary>
    public float Poll()
    {
        _input.TrimBacklog();
        int count = _input.Samples.Read(_read, 0, _read.Length);
        float peak = 0;
        for (int i = 0; i < count; i++)
        {
            peak = Math.Max(peak, Math.Abs(_read[i]));
        }
        _level = Math.Max(peak, _level * PollDecay);
        return _level;
    }

    public void Dispose() => _input.Dispose();
}
