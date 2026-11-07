namespace Zarpazo.History;

/// <param name="CanEdit">Whether the capture was saved with what it takes to open it again.</param>
internal sealed record HistoryEntry(string Path, DateTime Taken, bool CanEdit);
