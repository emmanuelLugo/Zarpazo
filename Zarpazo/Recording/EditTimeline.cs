namespace Zarpazo.Recording;

/// <summary>
/// The edit of a recording: an ordered list of pieces of the original, played one after the other. Cutting, deleting,
/// reordering, repeating and trimming a piece all change that list; every change can be undone. No window code here, so
/// the rules can be tested alone.
/// </summary>
internal sealed class EditTimeline
{
    /// <summary>No piece is shorter than this, so every piece can be seen and grabbed.</summary>
    public const double MinSeconds = 0.2;

    private readonly List<VideoSegment[]> _undo = new();
    private readonly List<VideoSegment[]> _redo = new();
    private VideoSegment[] _segments;
    private bool _inGesture;
    private bool _gestureSaved;

    /// <summary>Starts as the whole recording in one piece.</summary>
    public EditTimeline(double sourceDuration)
    {
        SourceDuration = Math.Max(0, sourceDuration);
        _segments = new[] { new VideoSegment(0, SourceDuration) };
    }

    /// <summary>Raised after any change, including undo and redo.</summary>
    public event EventHandler? Changed;

    /// <summary>The length of the original recording.</summary>
    public double SourceDuration { get; }

    public IReadOnlyList<VideoSegment> Segments => _segments;

    /// <summary>The length of the edited video: the pieces added up.</summary>
    public double Duration => _segments.Sum(segment => segment.Length);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when the pieces are no longer just the whole recording.</summary>
    public bool IsEdited => _segments.Length != 1 || _segments[0].Start > 0 || _segments[0].End < SourceDuration || _segments[0].Muted;

    /// <summary>Where in the edited video a piece begins.</summary>
    public double StartOf(int index)
    {
        double at = 0;
        for (int i = 0; i < index; i++)
        {
            at += _segments[i].Length;
        }
        return at;
    }

    /// <summary>The piece that plays at a moment of the edited video, and the matching moment in the original.</summary>
    public (int Index, double SourceTime) Locate(double time)
    {
        time = Math.Clamp(time, 0, Duration);
        double at = 0;
        for (int i = 0; i < _segments.Length; i++)
        {
            if (time < at + _segments[i].Length)
            {
                return (i, _segments[i].Start + (time - at));
            }
            at += _segments[i].Length;
        }
        return (_segments.Length - 1, _segments[^1].End); // the very end belongs to the last piece
    }

    /// <summary>The moment of the edited video where a point of a piece plays.</summary>
    public double ToTimeline(int index, double sourceTime) =>
        StartOf(index) + Math.Clamp(sourceTime - _segments[index].Start, 0, _segments[index].Length);

    /// <summary>Whether the piece playing at a moment can be cut there: both halves would be long enough.</summary>
    public bool CanSplit(double time)
    {
        (int index, double at) = Locate(time);
        VideoSegment segment = _segments[index];
        return at - segment.Start >= MinSeconds && segment.End - at >= MinSeconds;
    }

    /// <summary>Cuts the piece playing at a moment in two. Refused if either half would be too short.</summary>
    public bool Split(double time)
    {
        if (!CanSplit(time))
        {
            return false;
        }

        (int index, double at) = Locate(time);
        VideoSegment segment = _segments[index];
        var list = _segments.ToList();
        list[index] = segment with { End = at };
        list.Insert(index + 1, segment with { Start = at });
        Apply(list);
        return true;
    }

    /// <summary>Takes a piece out; the ones after it move up. The last piece left cannot be deleted.</summary>
    public bool Delete(int index)
    {
        if (_segments.Length <= 1 || !IsValid(index))
        {
            return false;
        }

        var list = _segments.ToList();
        list.RemoveAt(index);
        Apply(list);
        return true;
    }

    /// <summary>Moves a piece to another place in the order.</summary>
    public bool Move(int from, int to)
    {
        if (!IsValid(from) || !IsValid(to) || from == to)
        {
            return false;
        }

        var list = _segments.ToList();
        VideoSegment segment = list[from];
        list.RemoveAt(from);
        list.Insert(to, segment);
        Apply(list);
        return true;
    }

    /// <summary>Repeats a piece right after itself.</summary>
    public bool Duplicate(int index)
    {
        if (!IsValid(index))
        {
            return false;
        }

        var list = _segments.ToList();
        list.Insert(index + 1, list[index]);
        Apply(list);
        return true;
    }

    /// <summary>Moves where a piece starts. It can go back into material cut away before, but not past the end of the piece.</summary>
    public bool TrimStart(int index, double start)
    {
        if (!IsValid(index))
        {
            return false;
        }

        VideoSegment segment = _segments[index];
        start = Math.Clamp(start, 0, Math.Max(0, segment.End - MinSeconds));
        return Replace(index, segment with { Start = start });
    }

    /// <summary>Moves where a piece ends. It can go on into material cut away after, but not before the start of the piece.</summary>
    public bool TrimEnd(int index, double end)
    {
        if (!IsValid(index))
        {
            return false;
        }

        VideoSegment segment = _segments[index];
        end = Math.Clamp(end, Math.Min(SourceDuration, segment.Start + MinSeconds), SourceDuration);
        return Replace(index, segment with { End = end });
    }

    public bool SetMuted(int index, bool muted) => IsValid(index) && Replace(index, _segments[index] with { Muted = muted });

    /// <summary>Goes back to the whole recording, as one change that can be undone.</summary>
    public bool Reset()
    {
        if (!IsEdited)
        {
            return false;
        }
        Apply(new List<VideoSegment> { new(0, SourceDuration) });
        return true;
    }

    /// <summary>
    /// Groups the changes made until <see cref="EndGesture"/> into one undo step, for a drag that changes the same piece
    /// many times a second.
    /// </summary>
    public void BeginGesture()
    {
        _inGesture = true;
        _gestureSaved = false;
    }

    public void EndGesture() => _inGesture = false;

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(_segments);
        _segments = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(_segments);
        _segments = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsValid(int index) => index >= 0 && index < _segments.Length;

    private bool Replace(int index, VideoSegment segment)
    {
        if (segment == _segments[index])
        {
            return false;
        }

        var list = _segments.ToList();
        list[index] = segment;
        Apply(list);
        return true;
    }

    private void Apply(List<VideoSegment> list)
    {
        if (!_inGesture || !_gestureSaved)
        {
            _undo.Add(_segments);
            _gestureSaved = _inGesture;
        }
        _redo.Clear();
        _segments = list.ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
