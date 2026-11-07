using System.IO;
namespace Zarpazo.Recording;

/// <summary>The compression GIF uses for the pixels of a frame (variable-length LZW), written as the sub-blocks of the file.</summary>
internal static class GifLzw
{
    private const int MaxCodes = 4096; // codes are at most 12 bits
    private const int MaxBlock = 255;

    public static void Write(Stream output, ReadOnlySpan<byte> indices, int minCodeSize)
    {
        output.WriteByte((byte)minCodeSize);

        int clear = 1 << minCodeSize;
        int end = clear + 1;
        int nextCode = end + 1;
        int codeSize = minCodeSize + 1;
        var table = new Dictionary<int, int>();
        var bits = new BitWriter(output);

        bits.Write(clear, codeSize);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int key = (prefix << 8) | indices[i];
            if (table.TryGetValue(key, out int known))
            {
                prefix = known;
                continue;
            }

            bits.Write(prefix, codeSize);
            if (nextCode < MaxCodes)
            {
                table[key] = nextCode;
                if (nextCode == 1 << codeSize)
                {
                    codeSize++; // the reader grows its codes at the same point
                }
                nextCode++;
            }
            else
            {
                bits.Write(clear, codeSize); // table full: start over
                table.Clear();
                codeSize = minCodeSize + 1;
                nextCode = end + 1;
            }
            prefix = indices[i];
        }

        bits.Write(prefix, codeSize);
        bits.Write(end, codeSize);
        bits.Flush();
        output.WriteByte(0); // block terminator
    }

    // Codes go in least significant bit first and are cut into blocks of up to 255 bytes, each led by its length.
    private sealed class BitWriter(Stream output)
    {
        private readonly byte[] _block = new byte[MaxBlock];
        private int _length;
        private int _buffer;
        private int _bitCount;

        public void Write(int code, int size)
        {
            _buffer |= code << _bitCount;
            _bitCount += size;
            while (_bitCount >= 8)
            {
                Add((byte)(_buffer & 0xFF));
                _buffer >>= 8;
                _bitCount -= 8;
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                Add((byte)(_buffer & 0xFF));
                _buffer = 0;
                _bitCount = 0;
            }
            if (_length > 0)
            {
                EmitBlock();
            }
        }

        private void Add(byte value)
        {
            _block[_length++] = value;
            if (_length == MaxBlock)
            {
                EmitBlock();
            }
        }

        private void EmitBlock()
        {
            output.WriteByte((byte)_length);
            output.Write(_block, 0, _length);
            _length = 0;
        }
    }
}
