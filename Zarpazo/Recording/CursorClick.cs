namespace Zarpazo.Recording;

/// <summary>A mouse click during the recording: where the button went down, in pixels of the video, and how long it was held.</summary>
/// <param name="TimeMs">When the button went down, on the clock of the video.</param>
/// <param name="EndMs">When it came back up; the same as <paramref name="TimeMs"/> for a click too quick to see it held.</param>
internal readonly record struct CursorClick(long TimeMs, long EndMs, int X, int Y, ClickButton Button);
