using System.IO;

namespace Zarpazo.Recording;

/// <summary>
/// The names of a finished recording's files: the clean recording (without the pointer) is kept as "name.original.mp4"
/// with its track, for the editor to work on, and the finished video takes the plain name.
/// </summary>
internal static class RecordingFiles
{
    public const string OriginalSuffix = ".original";

    /// <summary>The clean recording kept for <paramref name="video"/>: "Grabación.mp4" keeps it in "Grabación.original.mp4".</summary>
    public static string OriginalFor(string video) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(video))!, Path.GetFileNameWithoutExtension(video) + OriginalSuffix + Path.GetExtension(video));

    /// <summary>The name a video would have without the suffix of a kept original: "Grabación.original" gives "Grabación".</summary>
    public static string WithoutOriginalSuffix(string name) =>
        name.EndsWith(OriginalSuffix, StringComparison.OrdinalIgnoreCase) ? name[..^OriginalSuffix.Length] : name;
}
