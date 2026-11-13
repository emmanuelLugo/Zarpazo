using NAudio.Wave;

namespace Zarpazo.Recording;

/// <summary>
/// Brings a microphone to a normal speaking volume whatever the device sends: most microphones come in far quieter
/// than the sound of Windows. It follows how loud the voice is and turns it up (or down) gradually; silence and
/// background noise leave the volume where it was, so the hiss is not pumped up between sentences.
/// </summary>
internal sealed class VoiceLeveler : ISampleProvider
{
    public const float TargetLevel = 0.2f; // where the loud parts of the voice end up (RMS), about -14 dB
    public const float MaxGain = 32f; // +30 dB: beyond this it is mostly noise that gets louder
    private const float MinGain = 0.25f;
    private const float NoiseFloor = 0.003f; // -50 dB: quieter than this is not voice
    private const float VoiceGate = 0.1f; // nor is anything 20 dB under the voice heard so far
    private const double BlockSeconds = 0.01;
    private const float LevelAttack = 0.3f; // per block: a louder voice is followed within a few hundredths of a second
    private const float LevelRelease = 0.004f; // a quieter one over a couple of seconds, so pauses between words don't count
    private const float LevelRecovery = 0.0005f; // sound under the gate: only so that one bang doesn't leave the voice low for good
    private const double GainUpSeconds = 0.25;
    private const double GainDownSeconds = 0.03;

    private readonly ISampleProvider _source;
    private readonly PeakLimiter _limiter;
    private readonly int _blockSamples;
    private readonly float _gainUp;
    private readonly float _gainDown;
    private float _level; // how loud the voice is, as it comes; 0 until it is first heard
    private float _target = 1f;
    private float _gain = 1f;
    private int _blockFill;
    private double _blockSquares;

    public VoiceLeveler(ISampleProvider source)
    {
        _source = source;
        int rate = source.WaveFormat.SampleRate;
        _limiter = new PeakLimiter(channels: source.WaveFormat.Channels, sampleRate: rate);
        _blockSamples = (int)(rate * BlockSeconds) * source.WaveFormat.Channels;
        _gainUp = (float)(1 - Math.Exp(-1 / (GainUpSeconds * rate * source.WaveFormat.Channels)));
        _gainDown = (float)(1 - Math.Exp(-1 / (GainDownSeconds * rate * source.WaveFormat.Channels)));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = offset; i < offset + read; i++)
        {
            float sample = buffer[i];
            _blockSquares += sample * sample;
            if (++_blockFill == _blockSamples)
            {
                Follow((float)Math.Sqrt(_blockSquares / _blockSamples));
                _blockFill = 0;
                _blockSquares = 0;
            }

            _gain += (_target - _gain) * (_target > _gain ? _gainUp : _gainDown);
            buffer[i] = sample * _gain;
        }
        _limiter.Process(buffer, offset, read);
        return read;
    }

    // Takes in how loud the last hundredth of a second was and decides the volume to move towards.
    private void Follow(float rms)
    {
        if (rms < NoiseFloor)
        {
            return;
        }

        if (_level == 0)
        {
            _level = rms; // the first thing heard: start from it instead of creeping up from nothing
        }
        else if (rms > _level)
        {
            _level += (rms - _level) * LevelAttack;
        }
        else
        {
            _level += (rms - _level) * (rms > _level * VoiceGate ? LevelRelease : LevelRecovery);
        }
        _target = Math.Clamp(TargetLevel / _level, MinGain, MaxGain);
    }
}
