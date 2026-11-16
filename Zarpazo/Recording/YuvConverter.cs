using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Zarpazo.Recording;

/// <summary>Turns a decoded NV12 picture (what video decoders produce) into BGRA, with the colour formula the video was made with.</summary>
internal sealed class YuvConverter
{
    // Fixed point, 8 bits of fraction: how much brightness and each colour difference add to red, green and blue.
    private readonly int _luma;
    private readonly int _black;
    private readonly int _redFromV;
    private readonly int _greenFromU;
    private readonly int _greenFromV;
    private readonly int _blueFromU;

    /// <param name="hd">BT.709 (HD video) instead of BT.601.</param>
    /// <param name="fullRange">Levels from 0 to 255 instead of 16 to 235.</param>
    public YuvConverter(bool hd, bool fullRange)
    {
        // The red and blue weights of the standard; green is what is left.
        (double kr, double kb) = hd ? (0.2126, 0.0722) : (0.299, 0.114);
        double kg = 1 - kr - kb;
        double lumaScale = fullRange ? 1 : 255.0 / 219;
        double chromaScale = fullRange ? 1 : 255.0 / 224;
        _black = fullRange ? 0 : 16;
        _luma = (int)Math.Round(lumaScale * 256);
        _redFromV = (int)Math.Round(2 * (1 - kr) * chromaScale * 256);
        _blueFromU = (int)Math.Round(2 * (1 - kb) * chromaScale * 256);
        _greenFromU = (int)Math.Round(2 * (1 - kb) * kb / kg * chromaScale * 256);
        _greenFromV = (int)Math.Round(2 * (1 - kr) * kr / kg * chromaScale * 256);
    }

    /// <param name="nv12">The brightness rows, <paramref name="pitch"/> bytes apart, then from <paramref name="chromaStart"/> the colour
    /// rows, half as many and the same distance apart: U and V side by side for every two pixels.</param>
    public void ToBgra(byte[] nv12, int pitch, int chromaStart, byte[] into, int width, int height)
    {
        int stride = width * 4;
        // Four pixels at a time with the same whole-number arithmetic as the one-at-a-time tail below, so both give the same bytes.
        Vector128<int> luma = Vector128.Create(_luma), black = Vector128.Create(_black), centre = Vector128.Create(128);
        Vector128<int> blueFromU = Vector128.Create(_blueFromU), greenFromU = Vector128.Create(_greenFromU);
        Vector128<int> greenFromV = Vector128.Create(_greenFromV), redFromV = Vector128.Create(_redFromV);
        Vector128<int> round = Vector128.Create(128), top = Vector128.Create(255), alpha = Vector128.Create(unchecked((int)0xFF000000));
        Parallel.For(0, height, row =>
        {
            int y = row * pitch;
            int uv = chromaStart + row / 2 * pitch;
            int o = row * stride;
            int x = 0;
            for (; x + 4 <= width; x += 4, o += 16)
            {
                Vector128<int> c = (Widen(MemoryMarshal.Read<uint>(nv12.AsSpan(y + x, 4))) - black) * luma;
                Vector128<int> pairs = Widen(MemoryMarshal.Read<uint>(nv12.AsSpan(uv + x, 4))); // u0 v0 u1 v1
                Vector128<int> d = Vector128.Shuffle(pairs, Vector128.Create(0, 0, 2, 2)) - centre;
                Vector128<int> e = Vector128.Shuffle(pairs, Vector128.Create(1, 1, 3, 3)) - centre;
                Vector128<int> b = Clamp(Vector128.ShiftRightArithmetic(c + blueFromU * d + round, 8), top);
                Vector128<int> g = Clamp(Vector128.ShiftRightArithmetic(c - greenFromU * d - greenFromV * e + round, 8), top);
                Vector128<int> r = Clamp(Vector128.ShiftRightArithmetic(c + redFromV * e + round, 8), top);
                Vector128<int> pixels = b | Vector128.ShiftLeft(g, 8) | Vector128.ShiftLeft(r, 16) | alpha;
                pixels.AsByte().CopyTo(into.AsSpan(o, 16));
            }
            for (; x < width; x++, o += 4)
            {
                int c = (nv12[y + x] - _black) * _luma;
                int pair = uv + (x & ~1);
                int d = nv12[pair] - 128;
                int e = nv12[pair + 1] - 128;
                into[o] = Clamp((c + _blueFromU * d + 128) >> 8);
                into[o + 1] = Clamp((c - _greenFromU * d - _greenFromV * e + 128) >> 8);
                into[o + 2] = Clamp((c + _redFromV * e + 128) >> 8);
                into[o + 3] = 255;
            }
        });
    }

    // Four bytes (as one number) to four whole numbers.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Widen(uint four) =>
        Vector128.WidenLower(Vector128.WidenLower(Vector128.CreateScalar(four).AsByte())).AsInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Clamp(Vector128<int> value, Vector128<int> top) => Vector128.Min(Vector128.Max(value, Vector128<int>.Zero), top);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
