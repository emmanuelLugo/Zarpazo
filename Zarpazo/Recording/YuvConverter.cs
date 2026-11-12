using System.Runtime.CompilerServices;

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
        Parallel.For(0, height, row =>
        {
            int y = row * pitch;
            int uv = chromaStart + row / 2 * pitch;
            int o = row * stride;
            for (int x = 0; x < width; x++, o += 4)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
