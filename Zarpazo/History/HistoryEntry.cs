namespace Zarpazo.History;

/// <param name="Path">The picture: the capture itself, or the first frame of a recording.</param>
/// <param name="CanEdit">Whether the capture was saved with what it takes to open it again.</param>
/// <param name="MediaPath">For a recording, the GIF or video file that goes with the picture; null for a capture.</param>
/// <param name="Kept">Whether the person marked it to keep: the limits and the age do not delete it.</param>
internal sealed record HistoryEntry(string Path, DateTime Taken, bool CanEdit, string? MediaPath = null, bool Kept = false)
{
    public bool IsRecording => MediaPath != null;

    /// <summary>The short date under a picture: "Hoy, 14:32", "Ayer, 09:20", the day and time this year, the full day before.</summary>
    public static string DateLabel(DateTime taken, DateTime now) => (now.Date - taken.Date).Days switch
    {
        0 => $"Hoy, {taken:HH:mm}",
        1 => $"Ayer, {taken:HH:mm}",
        _ when taken.Year == now.Year => $"{taken:dd'/'MM, HH:mm}",
        _ => $"{taken:dd'/'MM'/'yyyy}",
    };

    /// <summary>"GIF" or "Video" for a recording.</summary>
    public string Kind => System.IO.Path.GetExtension(MediaPath ?? "").Equals(".gif", StringComparison.OrdinalIgnoreCase) ? "GIF" : "Video";
}
