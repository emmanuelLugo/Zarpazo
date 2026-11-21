using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Capture;

/// <summary>What happened when a new picture of the scrolled area was added.</summary>
internal enum ScrollStep
{
    /// <summary>New rows were joined on.</summary>
    Added,

    /// <summary>The picture is the same as before: the content has reached its end.</summary>
    End,

    /// <summary>The picture changed but could not be lined up with the one before (an animation, a video, a jump).</summary>
    Lost,
}

/// <summary>
/// Joins pictures of an area that is being scrolled into one tall picture. Each new picture is lined up with the previous one by the
/// rows they share, and only the rows that are new are kept.
/// </summary>
/// <remarks>
/// shortcut: a header or footer that stays put while the page scrolls is repeated in every piece, because only the moving rows are
/// matched. Cut it out of the area, or mask it, if that ever matters.
/// </remarks>
internal sealed class ScrollStitcher(int width, int height)
{
    private const int MinOverlapDivisor = 4;  // two pictures must share at least a quarter of their height
    private const int MinMatches = 12;        // rows with content that have to agree to trust a shift
    private const double SameShare = 0.98;   // this much in common with the last picture means the scroll did nothing
    private const double MinAgreement = 0.6;  // share of the rows with content that must agree (a hover effect or a caret may change a few)

    /// <summary>Pixel rows, 4 bytes each; the kept pieces, in order, with the first being the whole first picture.</summary>
    private readonly List<byte[]> _pieces = [];
    private ulong[]? _previous;
    private int _lastShift;
    private int _totalHeight;

    public int Width => width;

    /// <summary>Height of the joined picture so far.</summary>
    public int TotalHeight => _totalHeight;

    /// <summary>How many rows the last picture moved by; 0 before the second one. Handy to size the next scroll.</summary>
    public int LastShift => _lastShift;

    /// <param name="pixels">The area as BGRA rows, <c>width * height * 4</c> bytes. It is copied if kept.</param>
    public ScrollStep Add(byte[] pixels)
    {
        ulong[] rows = RowHashes(pixels, width, height);
        if (_previous == null)
        {
            _pieces.Add((byte[])pixels.Clone());
            _totalHeight = height;
            Remember(rows);
            return ScrollStep.Added;
        }

        // Nothing moved. A blinking caret or a hover effect may change a few rows, so a near match counts too.
        if (rows.AsSpan().SequenceEqual(_previous) || Agreement(_previous, rows, 0) is { Matches: >= MinMatches, Share: >= SameShare })
        {
            return ScrollStep.End;
        }

        int? shift = FindShift(_previous, rows, _lastShift);
        if (shift is not { } moved)
        {
            return ScrollStep.Lost;
        }

        // The last `moved` rows of the new picture are the ones the old one did not have.
        var fresh = new byte[moved * width * 4];
        Buffer.BlockCopy(pixels, (height - moved) * width * 4, fresh, 0, fresh.Length);
        _pieces.Add(fresh);
        _totalHeight += moved;
        _lastShift = moved;
        Remember(rows);
        return ScrollStep.Added;
    }

    /// <summary>The joined picture. Null until one picture has been added.</summary>
    public BitmapSource? Result()
    {
        if (_pieces.Count == 0)
        {
            return null;
        }

        var all = new byte[(long)_totalHeight * width * 4];
        long at = 0;
        foreach (byte[] piece in _pieces)
        {
            Buffer.BlockCopy(piece, 0, all, (int)at, piece.Length);
            at += piece.Length;
        }

        BitmapSource image = BitmapSource.Create(width, _totalHeight, 96, 96, PixelFormats.Bgra32, null, all, width * 4);
        image.Freeze();
        return image;
    }

    private void Remember(ulong[] rows) => _previous = rows;

    /// <summary>Whether two pictures are the same, row for row.</summary>
    public static bool Same(byte[] a, byte[] b, int width, int height) =>
        RowHashes(a, width, height).AsSpan().SequenceEqual(RowHashes(b, width, height));

    /// <summary>
    /// By how many rows the content moved up from <paramref name="old"/> to <paramref name="next"/>, or null if no shift lines them up.
    /// Rows of one flat colour say nothing (a blank page matches at any shift), so only rows with content count.
    /// </summary>
    internal static int? FindShift(ulong[] old, ulong[] next, int hint)
    {
        int rows = old.Length;
        int maxShift = rows - rows / MinOverlapDivisor;

        // Scrolling by the same amount each time is the usual case, and a repeating pattern can fool the search: try the last shift first.
        if (hint is > 0 && hint <= maxShift && Agreement(old, next, hint) is { } known && known.Matches >= MinMatches && known.Share >= MinAgreement)
        {
            return hint;
        }

        int best = 0;
        int bestMatches = 0;
        for (int shift = 1; shift <= maxShift; shift++)
        {
            if (Agreement(old, next, shift) is { } found && found.Matches >= MinMatches && found.Share >= MinAgreement && found.Matches > bestMatches)
            {
                best = shift;
                bestMatches = found.Matches;
            }
        }
        return best == 0 ? null : best;
    }

    // Row y of the new picture was row y + shift of the old one. Zero marks a flat row.
    private static (int Matches, double Share)? Agreement(ulong[] old, ulong[] next, int shift)
    {
        int content = 0;
        int matches = 0;
        for (int y = 0; y < old.Length - shift; y++)
        {
            if (next[y] == 0)
            {
                continue;
            }
            content++;
            if (next[y] == old[y + shift])
            {
                matches++;
            }
        }
        return content == 0 ? null : (matches, (double)matches / content);
    }

    /// <summary>One number per row, 0 for a row that is a single colour (it has no content to line up by).</summary>
    internal static ulong[] RowHashes(byte[] pixels, int width, int height)
    {
        var hashes = new ulong[height];
        Span<uint> all = MemoryMarshal.Cast<byte, uint>(pixels);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<uint> row = all.Slice(y * width, width);
            ulong hash = 14695981039346656037UL;
            bool flat = true;
            foreach (uint pixel in row)
            {
                flat &= pixel == row[0];
                hash = (hash ^ pixel) * 1099511628211UL;
            }
            hashes[y] = flat ? 0 : hash | 1; // never 0 for a row with content
        }
        return hashes;
    }
}
