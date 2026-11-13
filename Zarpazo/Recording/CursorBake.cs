using System.IO;

namespace Zarpazo.Recording;

/// <summary>
/// A video is recorded without the pointer, and the one people copy or save straight away needs it: this draws it in, in
/// the style last chosen in the editor. The recording without it is kept beside, as "name.original.mp4" with its track,
/// for the editor to draw the pointer again in another style.
/// </summary>
internal static class CursorBake
{
    public const string OriginalSuffix = ".original";

    /// <summary>The clean recording kept for <paramref name="video"/>: "Grabación.mp4" keeps it in "Grabación.original.mp4".</summary>
    public static string OriginalFor(string video) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(video))!, Path.GetFileNameWithoutExtension(video) + OriginalSuffix + Path.GetExtension(video));

    /// <summary>The name a video would have without the suffix of a kept original: "Grabación.original" gives "Grabación".</summary>
    public static string WithoutOriginalSuffix(string name) =>
        name.EndsWith(OriginalSuffix, StringComparison.OrdinalIgnoreCase) ? name[..^OriginalSuffix.Length] : name;

    /// <summary>
    /// Draws the pointer into <paramref name="video"/>, which keeps its name; the clean recording moves to
    /// <see cref="OriginalFor"/>. Returns where the clean one went, or null when there was nothing to draw (no track, the
    /// pointer already in the pictures, or switched off) and the video was left as it was.
    /// </summary>
    /// <param name="progress">Reports 0 to 1 while it works.</param>
    /// <exception cref="InvalidOperationException">Windows could not write the video; the clean recording is back in its place.</exception>
    public static async Task<string?> ApplyAsync(string video, CursorStyle style, IProgress<double>? progress = null)
    {
        CursorTrack? track = CursorTrack.Load(video);
        var whole = new VideoSegment(0, double.MaxValue);
        CursorLayer? layer = CursorLayer.For(track, style, [whole]);
        if (layer == null)
        {
            return null;
        }

        string original = OriginalFor(video);
        File.Move(video, original, overwrite: true);
        File.Move(CursorTrack.PathFor(video), CursorTrack.PathFor(original), overwrite: true);
        try
        {
            await ZoomRenderer.RenderAsync(original, video, new ZoomCamera([whole], []), frame: null, progress, layer);
            return original;
        }
        catch
        {
            // Back as it was: a video without the pointer is still better than none.
            File.Move(original, video, overwrite: true);
            File.Move(CursorTrack.PathFor(original), CursorTrack.PathFor(video), overwrite: true);
            throw;
        }
    }
}
