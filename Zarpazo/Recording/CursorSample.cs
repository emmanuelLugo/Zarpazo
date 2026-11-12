namespace Zarpazo.Recording;

/// <summary>Where the pointer was at a moment of the recording, in pixels of the video (it can be outside the picture).</summary>
/// <param name="TimeMs">From the start of the recording, pauses left out: the same clock as the video.</param>
internal readonly record struct CursorSample(long TimeMs, int X, int Y);
