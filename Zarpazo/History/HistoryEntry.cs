namespace Zarpazo.History;

/// <param name="Path">The picture: the capture itself, or the first frame of a recording.</param>
/// <param name="CanEdit">Whether the capture was saved with what it takes to open it again.</param>
/// <param name="MediaPath">For a recording, the GIF or video file that goes with the picture; null for a capture.</param>
internal sealed record HistoryEntry(string Path, DateTime Taken, bool CanEdit, string? MediaPath = null)
{
    public bool IsRecording => MediaPath != null;

    /// <summary>"GIF" or "Video" for a recording.</summary>
    public string Kind => System.IO.Path.GetExtension(MediaPath ?? "").Equals(".gif", StringComparison.OrdinalIgnoreCase) ? "GIF" : "Video";
}
