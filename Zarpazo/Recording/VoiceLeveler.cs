using NAudio.Dsp;
using NAudio.Wave;

namespace Zarpazo.Recording;

/// <summary>
/// Brings a microphone to a normal speaking volume whatever the device sends: most microphones come in far quieter
/// than the sound of Windows. It follows how loud the voice is and turns it up (or down) gradually; silence and
/// background noise leave the volume where it was, so the hiss is not pumped up between sentences.
/// It also clears the voice up a little: the rumble under it is taken out and the high part, where the consonants
/// are, is raised, since cheap microphones and phones used as one sound muffled.
/// </summary>
internal sealed class VoiceLeveler : ISampleProvider
{
    public const float TargetLevel = 0.2f; // where the loud parts of the voice end up (RMS), about -14 dB
    public const float MaxGain = 32f; // +30 dB: beyond this it is mostly noise that gets louder
    private const float MinGain = 0.25f;
    private const float StartLevel = 0.05f; // what the voice is taken to be until it is heard: a boost of 4, never the maximum
    private const float NoiseFloor = 0.003f; // -50 dB: quieter than this is not voice
    private const float VoiceOverNoise = 4f; // nor is anything less than 12 dB over the background noise
    private const float NoiseRise = 1.0005f; // per block: the noise estimate doubles in about 14 seconds if the room gets louder
    private const float VoiceGate = 0.1f; // nor anything 20 dB under the voice heard so far
    private const double BlockSeconds = 0.01;
    private const float LevelAttack = 0.3f; // per block: a louder voice is followed within a few hundredths of a second
    private const float LevelRelease = 0.004f; // a quieter one over a couple of seconds, so pauses between words don't count
    private const float LevelSettling = 0.03f; // but ten times faster for the first second of voice, to find its volume
    private const int SettlingBlocks = 100;
    private const float LevelRecovery = 0.0005f; // sound under the gate: only so that one bang doesn't leave the voice low for good
    private const double GainUpSeconds = 0.25;
    private const double GainDownSeconds = 0.03;
    private const float RumbleCutHz = 100f;
    private const float ClarityFromHz = 3000f;
    private const float ClarityDb = 5f;

    private readonly ISampleProvider _source;
    private readonly PeakLimiter _limiter;
    private readonly int _blockSamples;
    private readonly float _gainUp;
    private readonly float _gainDown;
    private readonly BiQuadFilter[] _rumbleCut;
    private readonly BiQuadFilter[] _clarity;
    private float _level = StartLevel; // how loud the voice is, as it comes
    private float _noise = float.MaxValue; // how loud the room is when nobody speaks
    private int _voiceBlocks;
    private float _target = TargetLevel / StartLevel;
    private float _gain = TargetLevel / StartLevel;
    private int _blockFill;
    private double _blockSquares;

    public VoiceLeveler(ISampleProvider source)
    {
        _source = source;
        int rate = source.WaveFormat.SampleRate;
        _limiter = new PeakLimiter(channels: source.WaveFormat.Channels, sampleRate: rate);
        _blockSamples = (int)(rate * BlockSeconds) * source.WaveFormat.Channels;
        _rumbleCut = Enumerable.Range(0, source.WaveFormat.Channels).Select(_ => BiQuadFilter.HighPassFilter(rate, RumbleCutHz, 0.707f)).ToArray();
        _clarity = Enumerable.Range(0, source.WaveFormat.Channels).Select(_ => BiQuadFilter.HighShelf(rate, ClarityFromHz, 0.7f, ClarityDb)).ToArray();
        _gainUp = (float)(1 - Math.Exp(-1 / (GainUpSeconds * rate * source.WaveFormat.Channels)));
        _gainDown = (float)(1 - Math.Exp(-1 / (GainDownSeconds * rate * source.WaveFormat.Channels)));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = offset; i < offset + read; i++)
        {
            int channel = (i - offset) % _rumbleCut.Length;
            float sample = _clarity[channel].Transform(_rumbleCut[channel].Transform(buffer[i]));
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
        // The quietest the room has been lately is its noise; anything well over it is somebody speaking.
        _noise = rms < _noise ? rms : _noise * NoiseRise;
        if (rms < NoiseFloor || rms < _noise * VoiceOverNoise)
        {
            return;
        }

        if (rms > _level)
        {
            _level += (rms - _level) * LevelAttack;
        }
        else if (rms > _level * VoiceGate)
        {
            _level += (rms - _level) * (_voiceBlocks < SettlingBlocks ? LevelSettling : LevelRelease);
        }
        else
        {
            _level += (rms - _level) * LevelRecovery;
        }
        _voiceBlocks++;
        _target = Math.Clamp(TargetLevel / _level, MinGain, MaxGain);
    }
}
