namespace Zarpazo.Recording;

/// <summary>
/// The edit of a recording: an ordered list of pieces of the original, played one after the other, and the zooms. Cutting,
/// deleting, reordering, repeating and trimming a piece all change that list; every change, zooms and crop included, can be undone.
/// No window code here, so the rules can be tested alone.
/// </summary>
internal sealed class EditTimeline
{
    /// <summary>No piece is shorter than this, so every piece can be seen and grabbed.</summary>
    public const double MinSeconds = 0.2;

    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private VideoSegment[] _segments;
    private ZoomRegion[] _zooms = [];
    private VideoCrop _crop = VideoCrop.None;
    private VideoLook _look = VideoLook.Default;
    private ZoomRegion[] _startZooms = []; // the zooms the edit opened with, to tell whether anything changed
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

    /// <summary>The zooms, on the original's clock, in order and never overlapping.</summary>
    public IReadOnlyList<ZoomRegion> Zooms => _zooms;

    /// <summary>How much of each side of the picture is cut away; <see cref="VideoCrop.None"/> keeps it whole.</summary>
    public VideoCrop Crop => _crop;

    /// <summary>
    /// The frame, pointer and zoom style chosen. Changing it can be undone like the rest, so applying a preset is one step
    /// back; but it is a style, remembered for the next video, so it never counts as a change to discard on closing.
    /// </summary>
    public VideoLook Look => _look;

    /// <summary>The length of the edited video: the pieces added up.</summary>
    public double Duration => _segments.Sum(segment => segment.Length);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when the pieces are no longer just the whole recording.</summary>
    public bool IsEdited => _segments.Length != 1 || _segments[0].Start > 0 || _segments[0].End < SourceDuration || _segments[0].Muted;

    /// <summary>True when the edit is no longer as it opened: pieces changed, zooms other than the ones it started with, or a crop.</summary>
    public bool HasChanges => IsEdited || !_zooms.SequenceEqual(_startZooms) || !_crop.IsNone;

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

        _redo.Add(Current);
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(Current);
        Restore(_redo[^1]);
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

    private void Apply(List<VideoSegment> list) => Apply(list.ToArray(), _zooms);

    private void Apply(VideoSegment[] segments, ZoomRegion[] zooms) => Apply(segments, zooms, _crop, _look);

    private void Apply(VideoSegment[] segments, ZoomRegion[] zooms, VideoCrop crop) => Apply(segments, zooms, crop, _look);

    private void Apply(VideoSegment[] segments, ZoomRegion[] zooms, VideoCrop crop, VideoLook look)
    {
        if (!_inGesture || !_gestureSaved)
        {
            _undo.Add(Current);
            _gestureSaved = _inGesture;
        }
        _redo.Clear();
        _segments = segments;
        _zooms = zooms;
        _crop = crop;
        _look = look;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Snapshot Current => new(_segments, _zooms, _crop, _look);

    private void Restore(Snapshot snapshot) =>
        (_segments, _zooms, _crop, _look) = (snapshot.Segments, snapshot.Zooms, snapshot.Crop, snapshot.Look);

    // ---- Look ----

    /// <summary>The look the edit opens with (the one remembered from the last video): not a change, nothing to undo.</summary>
    public void StartWithLook(VideoLook look)
    {
        _look = look.Tidied();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Changes the look, as one step to undo (or part of a gesture's). A new zoom depth is given to every zoom of the video,
    /// so the zooms look as the style says. Returns false when nothing changed.
    /// </summary>
    public bool SetLook(VideoLook look)
    {
        look = look.Tidied();
        if (look == _look)
        {
            return false;
        }

        ZoomRegion[] zooms = look.Zoom.Depth == _look.Zoom.Depth
            ? _zooms
            : _zooms.Select(zoom => (zoom with { Scale = look.Zoom.Depth }).Clamped()).ToArray();
        Apply(_segments, zooms, _crop, look);
        return true;
    }

    // ---- Crop ----

    /// <summary>
    /// Changes the crop, tidied into range. A drag of a side sends many: inside <see cref="BeginGesture"/> they are one step
    /// to undo. Returns false when nothing changed.
    /// </summary>
    public bool SetCrop(VideoCrop crop)
    {
        crop = crop.Tidied();
        if (crop == _crop)
        {
            return false;
        }
        Apply(_segments, _zooms, crop);
        return true;
    }

    // ---- Zooms ----

    /// <summary>Replaces all the zooms at once (the automatic ones), as one change. Overlaps are trimmed away.</summary>
    public void SetZooms(IEnumerable<ZoomRegion> zooms)
    {
        List<ZoomRegion> list = Tidy(zooms);
        if (!list.SequenceEqual(_zooms))
        {
            Apply(_segments, list.ToArray());
        }
    }

    /// <summary>The zooms the edit starts with, before anything is done: they are not a change, so there is nothing to undo.</summary>
    public void StartWithZooms(IEnumerable<ZoomRegion> zooms)
    {
        _zooms = _startZooms = Tidy(zooms).ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // In order, in range and never overlapping: a zoom that starts inside the one before starts where that one ends.
    private List<ZoomRegion> Tidy(IEnumerable<ZoomRegion> zooms)
    {
        var list = new List<ZoomRegion>();
        foreach (ZoomRegion zoom in zooms.Select(zoom => zoom.Clamped()).OrderBy(zoom => zoom.Start))
        {
            double start = Math.Max(Math.Max(0, zoom.Start), list.Count > 0 ? list[^1].End : 0);
            double end = Math.Min(zoom.End, SourceDuration);
            if (end - start >= ZoomRegion.MinSeconds)
            {
                list.Add(zoom with { Start = start, End = end });
            }
        }
        return list;
    }

    /// <summary>
    /// Adds a zoom, shortened to fit the free space around it. Returns its place in <see cref="Zooms"/>, or -1 when there is
    /// no room (it falls on another zoom, or less than <see cref="ZoomRegion.MinSeconds"/> is free there).
    /// </summary>
    public int AddZoom(ZoomRegion zoom)
    {
        zoom = zoom.Clamped();
        double middle = (zoom.Start + zoom.End) / 2;
        if (_zooms.Any(other => middle >= other.Start && middle < other.End))
        {
            return -1;
        }

        (double lower, double upper) = FreeAround(middle);
        zoom = zoom with { Start = Math.Max(zoom.Start, lower), End = Math.Min(zoom.End, upper) };
        if (zoom.Length < ZoomRegion.MinSeconds)
        {
            return -1;
        }

        ZoomRegion[] list = _zooms.Append(zoom).OrderBy(other => other.Start).ToArray();
        Apply(_segments, list);
        return Array.IndexOf(list, zoom);
    }

    public bool DeleteZoom(int index)
    {
        if (!IsValidZoom(index))
        {
            return false;
        }
        Apply(_segments, _zooms.Where((_, i) => i != index).ToArray());
        return true;
    }

    /// <summary>Slides a zoom to start at <paramref name="start"/>, keeping its length, without passing its neighbours.</summary>
    public bool MoveZoom(int index, double start)
    {
        if (!IsValidZoom(index))
        {
            return false;
        }

        ZoomRegion zoom = _zooms[index];
        (double lower, double upper) = Neighbours(index);
        start = Math.Clamp(start, lower, Math.Max(lower, upper - zoom.Length));
        return ReplaceZoom(index, zoom with { Start = start, End = start + zoom.Length });
    }

    /// <summary>Moves where a zoom starts, not past the previous zoom nor too close to its own end.</summary>
    public bool SetZoomStart(int index, double start)
    {
        if (!IsValidZoom(index))
        {
            return false;
        }

        ZoomRegion zoom = _zooms[index];
        (double lower, _) = Neighbours(index);
        return ReplaceZoom(index, zoom with { Start = Math.Clamp(start, lower, zoom.End - ZoomRegion.MinSeconds) });
    }

    /// <summary>Moves where a zoom ends, not past the next zoom nor too close to its own start.</summary>
    public bool SetZoomEnd(int index, double end)
    {
        if (!IsValidZoom(index))
        {
            return false;
        }

        ZoomRegion zoom = _zooms[index];
        (_, double upper) = Neighbours(index);
        return ReplaceZoom(index, zoom with { End = Math.Clamp(end, zoom.Start + ZoomRegion.MinSeconds, upper) });
    }

    /// <summary>Changes the point a zoom looks at, in shares of the picture (0 to 1).</summary>
    public bool SetZoomFocus(int index, double x, double y) =>
        IsValidZoom(index) && ReplaceZoom(index, (_zooms[index] with { FocusX = x, FocusY = y }).Clamped());

    /// <summary>Changes how close a zoom goes.</summary>
    public bool SetZoomScale(int index, double scale) =>
        IsValidZoom(index) && ReplaceZoom(index, (_zooms[index] with { Scale = scale }).Clamped());

    private bool IsValidZoom(int index) => index >= 0 && index < _zooms.Length;

    private bool ReplaceZoom(int index, ZoomRegion zoom)
    {
        if (zoom == _zooms[index])
        {
            return false;
        }

        ZoomRegion[] list = (ZoomRegion[])_zooms.Clone();
        list[index] = zoom;
        Apply(_segments, list);
        return true;
    }

    // Where a zoom may reach: from the end of the one before to the start of the one after, inside the recording.
    private (double Lower, double Upper) Neighbours(int index) =>
        (index > 0 ? _zooms[index - 1].End : 0, index < _zooms.Length - 1 ? _zooms[index + 1].Start : SourceDuration);

    // The free stretch around a moment that no zoom covers.
    private (double Lower, double Upper) FreeAround(double time)
    {
        double lower = 0, upper = SourceDuration;
        foreach (ZoomRegion zoom in _zooms)
        {
            if (zoom.End <= time)
            {
                lower = Math.Max(lower, zoom.End);
            }
            else if (zoom.Start >= time)
            {
                upper = Math.Min(upper, zoom.Start);
            }
        }
        return (lower, upper);
    }

    /// <summary>Everything that can be undone, as it was at one moment.</summary>
    private sealed record Snapshot(VideoSegment[] Segments, ZoomRegion[] Zooms, VideoCrop Crop, VideoLook Look);
}
