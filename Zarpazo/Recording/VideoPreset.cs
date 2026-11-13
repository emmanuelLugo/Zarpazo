namespace Zarpazo.Recording;

/// <summary>A look saved with a name, to give the next videos the same style with one click.</summary>
internal sealed record VideoPreset(string Name, VideoLook Look)
{
    public const int MaxNameLength = 40;

    /// <summary>A name as it is kept: trimmed and not too long; null when nothing is left of it.</summary>
    public static string? TidyName(string? name)
    {
        string tidy = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (tidy.Length > MaxNameLength)
        {
            tidy = tidy[..MaxNameLength].TrimEnd();
        }
        return tidy.Length == 0 ? null : tidy;
    }
}
