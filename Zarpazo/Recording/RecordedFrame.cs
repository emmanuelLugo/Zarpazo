namespace Zarpazo.Recording;

/// <summary>
/// The part of the screen that changed since the previous frame, as tightly packed BGRA pixels.
/// Keeping only that rectangle is what lets a recording of a mostly still screen fit in memory.
/// </summary>
/// <param name="TimeMs">When it was taken, in milliseconds from the start of the recording.</param>
internal sealed record RecordedFrame(int X, int Y, int Width, int Height, byte[] Pixels, int TimeMs);
