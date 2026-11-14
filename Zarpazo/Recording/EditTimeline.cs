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
    private VideoOverlay[] _overlays = [];
    private VideoAspect _aspect = VideoAspect.Original;
    // What the edit opened with, to tell whether anything changed: the whole recording, the automatic zooms, no crop and no
    // overlays; or, for a project opened again, what it was saved with.
    private VideoSegment[] _startSegments;
    private ZoomRegion[] _startZooms = [];
    private VideoCrop _startCrop = VideoCrop.None;
    private VideoOverlay[] _startOverlays = [];
    private VideoAspect _startAspect = VideoAspect.Original;
    private bool _inGesture;
    private bool _gestureSaved;

    /// <summary>Starts as the whole recording in one piece.</summary>
    public EditTimeline(double sourceDuration)
    {
        SourceDuration = Math.Max(0, sourceDuration);
        _segments = _startSegments = new[] { new VideoSegment(0, SourceDuration) };
    }

    /// <summary>Raised after any change, including undo and redo.</summary>
    public event EventHandler? Changed;

    /// <summary>The length of the original recording.</summary>
    public double SourceDuration { get; }

    public IReadOnlyList<VideoSegment> Segments => _segments;

    /// <summary>The zooms, on the original's clock, in order and never overlapping.</summary>
    public IReadOnlyList<ZoomRegion> Zooms => _zooms;

    /// <summary>The blurs, texts and arrows over the video, on the original's clock; they may overlap, and the later one is drawn over the earlier.</summary>
    public IReadOnlyList<VideoOverlay> Overlays => _overlays;

    /// <summary>How much of each side of the picture is cut away; <see cref="VideoCrop.None"/> keeps it whole.</summary>
    public VideoCrop Crop => _crop;

    /// <summary>The shape of the finished video; <see cref="VideoAspect.Original"/> keeps the recording's (cropped) own.</summary>
    public VideoAspect Aspect => _aspect;

    /// <summary>
    /// The frame, pointer and zoom style chosen. Changing it can be undone like the rest, so applying a preset is one step
    /// back; but it is a style, remembered for the next video, so it never counts as a change to discard on closing.
    /// </summary>
    public VideoLook Look => _look;

    /// <summary>The length of the edited video: the pieces added up, each at its speed.</summary>
    public double Duration => _segments.Sum(segment => segment.PlayLength);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when the pieces are no longer just the whole recording.</summary>
    public bool IsEdited => _segments.Length != 1 || _segments[0].Start > 0 || _segments[0].End < SourceDuration || _segments[0].Muted
        || _segments[0].IsRetimed;

    /// <summary>True when the edit is no longer as it opened: pieces, zooms, crop or overlays other than the ones it started with.</summary>
    public bool HasChanges => !_segments.SequenceEqual(_startSegments) || !_zooms.SequenceEqual(_startZooms) || _crop != _startCrop
        || !_overlays.SequenceEqual(_startOverlays) || _aspect != _startAspect;

    /// <summary>Where in the edited video a piece begins.</summary>
    public double StartOf(int index)
    {
        double at = 0;
        for (int i = 0; i < index; i++)
        {
            at += _segments[i].PlayLength;
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
            if (time < at + _segments[i].PlayLength)
            {
                return (i, _segments[i].ToOriginal(time - at));
            }
            at += _segments[i].PlayLength;
        }
        return (_segments.Length - 1, _segments[^1].End); // the very end belongs to the last piece
    }

    /// <summary>The moment of the edited video where a point of a piece plays.</summary>
    public double ToTimeline(int index, double sourceTime) =>
        StartOf(index) + _segments[index].ToPlay(sourceTime);

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

    /// <summary>Changes how fast a piece plays, within the speeds offered. Returns false when nothing changed.</summary>
    public bool SetSpeed(int index, double speed) =>
        IsValid(index) && Replace(index, _segments[index] with { Speed = Math.Clamp(speed, VideoSegment.Speeds[0], VideoSegment.Speeds[^1]) });

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

    private void Apply(VideoSegment[] segments, ZoomRegion[] zooms, VideoCrop crop, VideoLook look, VideoOverlay[]? overlays = null, VideoAspect? aspect = null)
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
        _overlays = overlays ?? _overlays;
        _aspect = aspect ?? _aspect;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Snapshot Current => new(_segments, _zooms, _crop, _look, _overlays, _aspect);

    private void Restore(Snapshot snapshot) =>
        (_segments, _zooms, _crop, _look, _overlays, _aspect) =
            (snapshot.Segments, snapshot.Zooms, snapshot.Crop, snapshot.Look, snapshot.Overlays, snapshot.Aspect);

    // ---- Project ----

    /// <summary>
    /// Opens an edit that was saved: its pieces, zooms, overlays, crop and look, tidied (a file can come from anywhere) and with
    /// the times kept inside this recording. It is where the edit starts, so there is nothing to undo and no change to discard.
    /// A piece that does not fit is left out; with none left the edit starts as the whole recording.
    /// </summary>
    public void Load(IEnumerable<VideoSegment> segments, IEnumerable<ZoomRegion> zooms, VideoCrop crop, IEnumerable<VideoOverlay> overlays, VideoLook look,
        VideoAspect aspect = VideoAspect.Original)
    {
        VideoSegment[] kept = [.. segments
            .Where(segment => double.IsFinite(segment.Start) && double.IsFinite(segment.End))
            .Select(segment => segment with
            {
                Start = Math.Clamp(segment.Start, 0, SourceDuration),
                End = Math.Clamp(segment.End, 0, SourceDuration),
                Speed = Array.IndexOf(VideoSegment.Speeds, segment.Speed) >= 0 ? segment.Speed : 1,
            })
            .Where(segment => segment.Length >= MinSeconds)];
        _segments = _startSegments = kept.Length > 0 ? kept : [new VideoSegment(0, SourceDuration)];
        _zooms = _startZooms = Tidy(zooms.Where(zoom => double.IsFinite(zoom.Start) && double.IsFinite(zoom.End))).ToArray();
        _crop = _startCrop = crop.Tidied();
        _overlays = _startOverlays = [.. overlays
            .Where(overlay => double.IsFinite(overlay.Start) && double.IsFinite(overlay.End))
            .Select(overlay => FitInTime(overlay, out VideoOverlay fitted) ? (VideoOverlay?)fitted : null)
            .OfType<VideoOverlay>()];
        _look = look.Tidied();
        _aspect = _startAspect = Enum.IsDefined(aspect) ? aspect : VideoAspect.Original;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

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

    // ---- Aspect ----

    /// <summary>Changes the shape of the finished video, as one step to undo. Returns false when nothing changed.</summary>
    public bool SetAspect(VideoAspect aspect)
    {
        if (aspect == _aspect || !Enum.IsDefined(aspect))
        {
            return false;
        }
        Apply(_segments, _zooms, _crop, _look, aspect: aspect);
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

    // ---- Overlays ----

    /// <summary>
    /// Adds a blur, text or arrow, tidied and kept inside the recording (never shorter than <see cref="VideoOverlay.MinSeconds"/>).
    /// Returns its place in <see cref="Overlays"/>, or -1 when it falls outside the recording.
    /// </summary>
    public int AddOverlay(VideoOverlay overlay)
    {
        if (!FitInTime(overlay, out overlay))
        {
            return -1;
        }
        Apply(_segments, _zooms, _crop, _look, _overlays.Append(overlay).ToArray());
        return _overlays.Length - 1;
    }

    public bool DeleteOverlay(int index)
    {
        if (!IsValidOverlay(index))
        {
            return false;
        }
        Apply(_segments, _zooms, _crop, _look, _overlays.Where((_, i) => i != index).ToArray());
        return true;
    }

    /// <summary>Changes an overlay (its words, colour, size or points), tidied. Returns false when nothing changed.</summary>
    public bool SetOverlay(int index, VideoOverlay overlay) =>
        IsValidOverlay(index) && FitInTime(overlay, out overlay) && ReplaceOverlay(index, overlay);

    /// <summary>Slides an overlay to start at <paramref name="start"/>, keeping its length, inside the recording.</summary>
    public bool MoveOverlay(int index, double start)
    {
        if (!IsValidOverlay(index))
        {
            return false;
        }
        VideoOverlay overlay = _overlays[index];
        start = Math.Clamp(start, 0, Math.Max(0, SourceDuration - overlay.Length));
        return ReplaceOverlay(index, overlay with { Start = start, End = start + overlay.Length });
    }

    /// <summary>Moves where an overlay starts, not too close to its own end.</summary>
    public bool SetOverlayStart(int index, double start) =>
        IsValidOverlay(index) && ReplaceOverlay(index, _overlays[index] with { Start = Math.Clamp(start, 0, _overlays[index].End - VideoOverlay.MinSeconds) });

    /// <summary>Moves where an overlay ends, not too close to its own start.</summary>
    public bool SetOverlayEnd(int index, double end) =>
        IsValidOverlay(index) && ReplaceOverlay(index, _overlays[index] with { End = Math.Clamp(end, _overlays[index].Start + VideoOverlay.MinSeconds, SourceDuration) });

    private bool IsValidOverlay(int index) => index >= 0 && index < _overlays.Length;

    private bool ReplaceOverlay(int index, VideoOverlay overlay)
    {
        if (overlay == _overlays[index])
        {
            return false;
        }

        VideoOverlay[] list = (VideoOverlay[])_overlays.Clone();
        list[index] = overlay;
        Apply(_segments, _zooms, _crop, _look, list);
        return true;
    }

    // Tidies the values and puts the times inside the recording; false when no stretch of at least the minimum fits there.
    private bool FitInTime(VideoOverlay overlay, out VideoOverlay fitted)
    {
        fitted = overlay.Tidied();
        double start = Math.Clamp(fitted.Start, 0, SourceDuration);
        double end = Math.Clamp(fitted.End, 0, SourceDuration);
        if (end - start < VideoOverlay.MinSeconds)
        {
            return false;
        }
        fitted = fitted with { Start = start, End = end };
        return true;
    }

    /// <summary>Everything that can be undone, as it was at one moment.</summary>
    private sealed record Snapshot(VideoSegment[] Segments, ZoomRegion[] Zooms, VideoCrop Crop, VideoLook Look, VideoOverlay[] Overlays, VideoAspect Aspect);
}
