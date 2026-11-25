namespace Zarpazo.Recording;

/// <summary>The pointer became another one at a moment of the recording, on the clock of the video.</summary>
internal readonly record struct CursorKindChange(long TimeMs, CursorKind Kind);
