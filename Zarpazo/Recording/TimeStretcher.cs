using System.Buffers.Binary;
using System.IO;

namespace Zarpazo.Recording;

/// <summary>
/// Plays 16-bit stereo sound faster or slower without changing its pitch (WSOLA: overlapping 40 ms windows of the input are
/// laid end to end at the output's own pace, each one slid a little to where it best continues the one before).
/// Feed it the sound in any size of pieces with <see cref="Process"/>, and end with <see cref="Flush"/>.
/// </summary>
internal sealed class TimeStretcher
{
    private const int Window = 1920;               // 40 ms at 48 kHz
    private const int Hop = Window / 2;            // how far the output moves for every window
    private const int Tolerance = 480;             // a window may slide up to 10 ms to find a better fit
    private const int Decimation = 4;              // the fit is looked for on a quarter of the samples: far cheaper, and as good for the ear
    private const int BytesPerFrame = Mp4Writer.AudioChannels * 2;

    private static readonly float[] Fade = MakeFade();

    private readonly double _rate;
    private readonly double _inputHop;             // how far the input moves for every window
    private float[] _inLeft = new float[Window * 8];
    private float[] _inRight = new float[Window * 8];
    private float[] _inMono = new float[Window * 8]; // quarter of the sound, to look for the fit
    private long _inStart;                         // the input position that index 0 of the buffers holds
    private int _inCount;                          // samples in the buffers
    private long _inTotal;                         // every sample received so far
    private readonly float[] _outLeft = new float[Window];
    private readonly float[] _outRight = new float[Window];
    private long _outEmitted;                      // samples already handed back
    private long _frame;                           // the next window to place
    private long _lastStart = -1;                  // where in the input the last window began

    /// <param name="rate">2 plays twice as fast (half the length), 0.5 twice as slow.</param>
    public TimeStretcher(double rate)
    {
        _rate = rate > 0 ? rate : 1;
        _inputHop = Hop * _rate;
    }

    /// <summary>The sound as it will last once stretched, in samples, for <paramref name="input"/> samples in.</summary>
    public static long OutputLength(long input, double rate) => (long)Math.Round(input / (rate > 0 ? rate : 1));

    /// <summary>Takes more sound (16-bit stereo, 48 kHz) and returns the stretched sound that is ready; may be empty.</summary>
    public byte[] Process(byte[] pcm)
    {
        Append(pcm);
        var output = new MemoryStream();
        while (CanPlace(final: false))
        {
            PlaceWindow(output);
        }
        Trim();
        return output.ToArray();
    }

    /// <summary>Ends the sound: returns what is left, so that the whole is exactly as long as the input divided by the rate.</summary>
    public byte[] Flush()
    {
        long before = _outEmitted;
        var output = new MemoryStream();
        // Quiet after the end, so the last windows have something to read.
        Append(new byte[(Window + Tolerance + Hop) * BytesPerFrame], real: false);
        while (CanPlace(final: true))
        {
            PlaceWindow(output);
        }

        // The windows laid so far cover a little more than needed: cut to the exact length, or fill up if short.
        long wanted = Math.Max(0, OutputLength(_inTotal, _rate) - before);
        long have = output.Length / BytesPerFrame;
        // The end of the last window, which no window follows, comes out too (fading away), as far as it is needed.
        for (int i = 0; have < wanted && i < Window - Hop; i++, have++)
        {
            WriteFrame(output, _outLeft[i], _outRight[i]);
        }
        if (have < wanted)
        {
            output.Write(new byte[(wanted - have) * BytesPerFrame]);
        }
        output.SetLength(wanted * BytesPerFrame);
        _outEmitted = before + wanted;
        return output.ToArray();
    }

    private void Append(byte[] pcm, bool real = true)
    {
        int frames = pcm.Length / BytesPerFrame;
        if (frames == 0)
        {
            return;
        }
        if (_inCount + frames > _inLeft.Length)
        {
            int size = Math.Max(_inLeft.Length * 2, _inCount + frames);
            Array.Resize(ref _inLeft, size);
            Array.Resize(ref _inRight, size);
            Array.Resize(ref _inMono, size);
        }
        for (int i = 0; i < frames; i++)
        {
            short left = BitConverter.ToInt16(pcm, i * BytesPerFrame);
            short right = BitConverter.ToInt16(pcm, i * BytesPerFrame + 2);
            _inLeft[_inCount + i] = left / 32768f;
            _inRight[_inCount + i] = right / 32768f;
            _inMono[_inCount + i] = (left + right) / 65536f;
        }
        _inCount += frames;
        if (real)
        {
            _inTotal += frames;
        }
    }

    // Whether the next window can be placed: everything it may read has arrived (the end of the sound counts as arrived).
    private bool CanPlace(bool final)
    {
        long nominal = (long)Math.Round(_frame * _inputHop);
        if (final && nominal >= _inTotal)
        {
            return false;
        }
        return nominal + Tolerance + Window <= _inStart + _inCount;
    }

    private void PlaceWindow(MemoryStream output)
    {
        long nominal = (long)Math.Round(_frame * _inputHop);
        long start = _lastStart < 0 ? 0 : BestStart(nominal);
        if (start < _inStart)
        {
            start = _inStart;
        }

        int from = (int)(start - _inStart);
        for (int i = 0; i < Window; i++)
        {
            // The very first window has no window before it to blend with: its first half is kept whole.
            float weight = _frame == 0 && i < Hop ? 1f : Fade[i];
            _outLeft[i] += _inLeft[from + i] * weight;
            _outRight[i] += _inRight[from + i] * weight;
        }

        // The first half is now complete (the window before added its second half to it): hand it back.
        for (int i = 0; i < Hop; i++)
        {
            WriteFrame(output, _outLeft[i], _outRight[i]);
        }
        _outEmitted += Hop;
        Array.Copy(_outLeft, Hop, _outLeft, 0, Window - Hop);
        Array.Copy(_outRight, Hop, _outRight, 0, Window - Hop);
        Array.Clear(_outLeft, Window - Hop, Hop);
        Array.Clear(_outRight, Window - Hop, Hop);

        _lastStart = start;
        _frame++;
    }

    // Where, within the tolerance around the nominal position, the next window best continues the last one: the place whose
    // first half looks most like what followed the last window's first half in the input.
    private long BestStart(long nominal)
    {
        long target = _lastStart + Hop;
        long low = Math.Max(_inStart, nominal - Tolerance);
        long high = nominal + Tolerance;
        if (target + Hop > _inStart + _inCount)
        {
            return Math.Clamp(nominal, low, high);
        }

        int length = Hop / Decimation;
        int targetAt = (int)(target - _inStart) / Decimation * Decimation;
        double best = double.NegativeInfinity;
        long chosen = Math.Clamp(nominal, low, high);
        for (long candidate = low; candidate <= high; candidate += Decimation)
        {
            int at = (int)(candidate - _inStart);
            if (at + Hop > _inCount)
            {
                break;
            }
            double sum = 0, energy = 1e-9;
            for (int i = 0; i < length; i++)
            {
                float a = Mean(_inMono, targetAt + i * Decimation);
                float b = Mean(_inMono, at + i * Decimation);
                sum += a * b;
                energy += b * b;
            }
            // Normalised, so a loud stretch does not win just for being loud.
            double score = sum / Math.Sqrt(energy);
            if (score > best)
            {
                best = score;
                chosen = candidate;
            }
        }
        return chosen;
    }

    // The mean of a few neighbouring samples: a crude low-pass, so that skipping samples does not alias.
    private static float Mean(float[] samples, int at) => (samples[at] + samples[at + 1] + samples[at + 2] + samples[at + 3]) * 0.25f;

    // Drops the input that no window can read any more.
    private void Trim()
    {
        long nominal = (long)Math.Round(_frame * _inputHop);
        long keepFrom = Math.Min(_lastStart < 0 ? 0 : _lastStart + Hop, nominal - Tolerance);
        int drop = (int)Math.Min(_inCount, Math.Max(0, keepFrom - _inStart));
        if (drop < Window * 4)
        {
            return;
        }
        Array.Copy(_inLeft, drop, _inLeft, 0, _inCount - drop);
        Array.Copy(_inRight, drop, _inRight, 0, _inCount - drop);
        Array.Copy(_inMono, drop, _inMono, 0, _inCount - drop);
        _inStart += drop;
        _inCount -= drop;
    }

    private static void WriteFrame(MemoryStream output, float left, float right)
    {
        Span<byte> frame = stackalloc byte[BytesPerFrame];
        BinaryPrimitives.WriteInt16LittleEndian(frame, ToShort(left));
        BinaryPrimitives.WriteInt16LittleEndian(frame[2..], ToShort(right));
        output.Write(frame);
    }

    private static short ToShort(float value) => (short)Math.Clamp((int)Math.Round(value * 32767f), short.MinValue, short.MaxValue);

    // Hann window: two of them half a window apart add up to exactly 1.
    private static float[] MakeFade()
    {
        var fade = new float[Window];
        for (int i = 0; i < Window; i++)
        {
            fade[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Window));
        }
        return fade;
    }
}
