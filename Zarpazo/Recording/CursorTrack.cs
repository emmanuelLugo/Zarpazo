using System.IO;
using System.Text.Json;

namespace Zarpazo.Recording;

/// <summary>
/// Where the pointer went and where it clicked during a video, kept in a small file next to it. The editor uses it to zoom
/// in on what was clicked. Positions are in pixels of the video, times on the video's clock.
/// </summary>
/// <param name="Width">The size of the video the positions refer to.</param>
/// <param name="Moves">The pointer's position each time it moved, oldest first.</param>
/// <param name="Clicks">The clicks, oldest first.</param>
/// <param name="PointerInPicture">The pointer was painted into the pictures as they were recorded, so the editor must not
/// draw another. Recordings made before the editor drew its own have it, and files of theirs lack the field: hence true.</param>
/// <param name="PointerScale">How many pixels of the video the pointer on screen measured per pixel of its picture: the
/// video's size over the area's, times the screen's scaling (a 150 % screen has a pointer 1.5 times as big).</param>
internal sealed record CursorTrack(int Width, int Height, IReadOnlyList<CursorSample> Moves, IReadOnlyList<CursorClick> Clicks,
    bool PointerInPicture = true, double PointerScale = 1)
{
    public const string Extension = ".cursor.json";

    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>The file that goes with a video: "Grabación.mp4" keeps its pointer in "Grabación.cursor.json".</summary>
    public static string PathFor(string video) => Path.ChangeExtension(video, Extension);

    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Save(string video) => File.WriteAllText(PathFor(video), JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>The pointer recorded with a video; null when there is none (an older recording, or a GIF) or it cannot be read.</summary>
    public static CursorTrack? Load(string video)
    {
        try
        {
            string path = PathFor(video);
            if (!File.Exists(path))
            {
                return null;
            }

            CursorTrack? track = JsonSerializer.Deserialize<CursorTrack>(File.ReadAllText(path), JsonOptions);
            return track is { Width: > 0, Height: > 0, Moves: not null, Clicks: not null } ? track : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Where the pointer was at a moment, in shares of the picture (0 to 1, outside when it was off the picture); null if it never moved.</summary>
    public (double X, double Y)? PositionAt(long timeMs)
    {
        if (Moves.Count == 0)
        {
            return null;
        }

        // The last move at or before that moment: the pointer stays where it is until it moves again.
        int low = 0, high = Moves.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Moves[middle].TimeMs <= timeMs)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return (Moves[low].X / (double)Width, Moves[low].Y / (double)Height);
    }

    /// <summary>Copies the pointer of <paramref name="video"/>, if it has one, to go with <paramref name="copy"/>.</summary>
    public static void CopyAlong(string video, string copy)
    {
        string from = PathFor(video);
        if (File.Exists(from))
        {
            File.Copy(from, PathFor(copy), overwrite: true);
        }
    }
}
