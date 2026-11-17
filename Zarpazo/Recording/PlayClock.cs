namespace Zarpazo.Recording;

/// <summary>
/// The clock of a video whose pieces play at their own speeds, made from a file with those pieces joined end to end as
/// recorded (each at 1x). Turns a moment of the finished video into the moment of that file to show, and back.
/// </summary>
internal sealed class PlayClock
{
    private readonly VideoSegment[] _pieces;
    private readonly double[] _inputStarts;
    private readonly double[] _playStarts;

    public PlayClock(IReadOnlyList<VideoSegment> pieces)
    {
        _pieces = pieces.ToArray();
        _inputStarts = new double[_pieces.Length];
        _playStarts = new double[_pieces.Length];
        double input = 0, play = 0;
        for (int i = 0; i < _pieces.Length; i++)
        {
            _inputStarts[i] = input;
            _playStarts[i] = play;
            input += _pieces[i].Length;
            play += _pieces[i].PlayLength;
        }
        InputDuration = input;
        Duration = play;
    }

    /// <summary>How long the finished video lasts.</summary>
    public double Duration { get; }

    /// <summary>How long the joined file lasts.</summary>
    public double InputDuration { get; }

    /// <summary>The moment of the joined file shown at <paramref name="play"/> seconds of the finished video.</summary>
    public double ToInput(double play)
    {
        int i = Index(_playStarts, play);
        return i < 0 ? 0 : _inputStarts[i] + Math.Clamp(play - _playStarts[i], 0, _pieces[i].PlayLength) * _pieces[i].Rate;
    }

    /// <summary>The moment of the finished video where <paramref name="input"/> seconds of the joined file show.</summary>
    public double ToPlay(double input)
    {
        int i = Index(_inputStarts, input);
        return i < 0 ? 0 : _playStarts[i] + Math.Clamp(input - _inputStarts[i], 0, _pieces[i].Length) / _pieces[i].Rate;
    }

    /// <summary>How many pieces there are.</summary>
    public int Count => _pieces.Length;

    /// <summary>The piece that holds the moment <paramref name="input"/> of the joined file (the last one for anything after the end).</summary>
    public int PieceAt(double input) => Index(_inputStarts, input);

    /// <summary>The piece <paramref name="index"/>.</summary>
    public VideoSegment Piece(int index) => _pieces[index];

    /// <summary>Where in the joined file the piece <paramref name="index"/> ends.</summary>
    public double InputEnd(int index) => _inputStarts[index] + _pieces[index].Length;

    /// <summary>Where in the finished video the piece <paramref name="index"/> begins.</summary>
    public double PlayStart(int index) => _playStarts[index];

    /// <summary>Whether the piece at <paramref name="input"/> seconds of the joined file plays at another speed.</summary>
    public bool IsRetimedAt(double input)
    {
        int i = Index(_inputStarts, input);
        return i >= 0 && _pieces[i].IsRetimed;
    }

    // The last piece that starts at or before the moment.
    private static int Index(double[] starts, double at)
    {
        int found = starts.Length > 0 ? 0 : -1;
        for (int i = 1; i < starts.Length; i++)
        {
            if (starts[i] <= at)
            {
                found = i;
            }
        }
        return found;
    }
}
