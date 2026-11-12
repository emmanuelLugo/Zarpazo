using System.IO;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Zarpazo.Recording;

/// <summary>Writes an edited video: the pieces of a recording joined in order, with the editing engine that comes with Windows, and the zooms.</summary>
internal static class VideoExporter
{
    /// <summary>
    /// Writes <paramref name="target"/> with the <paramref name="segments"/> of <paramref name="source"/> one after the other,
    /// and the <paramref name="zooms"/> applied. The pieces are joined first (into a file next to the target, removed after)
    /// and the zooms drawn on the joined video; with nothing cut, straight from the recording.
    /// </summary>
    /// <param name="progress">Reports 0 to 1 while it works.</param>
    public static async Task ExportAsync(string source, string target, IReadOnlyList<VideoSegment> segments, IReadOnlyList<ZoomRegion> zooms,
        double sourceDuration, IProgress<double>? progress = null) =>
        await ExportAsync(source, target, segments, zooms, sourceDuration, frame: null, progress);

    /// <param name="frame">The frame to show the recording in; null or switched off for none.</param>
    public static async Task ExportAsync(string source, string target, IReadOnlyList<VideoSegment> segments, IReadOnlyList<ZoomRegion> zooms,
        double sourceDuration, VideoFrameStyle? frame, IProgress<double>? progress = null)
    {
        bool framed = frame is { Enabled: true };
        if (framed)
        {
            // The zooms look at points of the recording; in a frame the camera moves over the whole scene.
            MediaEncodingProfile size = await MediaEncodingProfile.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(source));
            zooms = FrameLayout.For(frame!, (int)size.Video.Width & ~1, (int)size.Video.Height & ~1).ToScene(zooms);
        }

        var camera = new ZoomCamera(segments, zooms);
        if (camera.IsEmpty && !framed)
        {
            await ExportAsync(source, target, segments, progress);
            return;
        }

        bool whole = segments.Count == 1 && segments[0].Start <= 0 && segments[0].End >= sourceDuration - 0.001 && !segments[0].Muted;
        if (whole)
        {
            await ZoomRenderer.RenderAsync(source, target, camera, frame, progress);
            return;
        }

        // Joining is quick next to drawing every picture again, so it takes the first fifth of the bar.
        string joined = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, Path.GetFileNameWithoutExtension(target) + ".partes.mp4");
        try
        {
            await ExportAsync(source, joined, segments, new Progress<double>(value => progress?.Report(value * 0.2)));
            await ZoomRenderer.RenderAsync(joined, target, camera, frame, new Progress<double>(value => progress?.Report(0.2 + value * 0.8)));
        }
        finally
        {
            try
            {
                File.Delete(joined);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Writes <paramref name="target"/> with the <paramref name="segments"/> of <paramref name="source"/> one after the other.</summary>
    /// <param name="progress">Reports 0 to 1 while it works.</param>
    /// <exception cref="ArgumentException">There are no pieces, or one is empty or lies outside the video.</exception>
    /// <exception cref="InvalidOperationException">Windows could not make the video.</exception>
    public static async Task ExportAsync(string source, string target, IReadOnlyList<VideoSegment> segments, IProgress<double>? progress = null)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("There must be at least one part to keep.", nameof(segments));
        }

        StorageFile file = await StorageFile.GetFileFromPathAsync(source);
        var composition = new MediaComposition();
        foreach (VideoSegment segment in segments)
        {
            // A clip of its own for every piece: the same file can be opened as many times as it is used.
            MediaClip clip = await MediaClip.CreateFromFileAsync(file);
            TimeSpan length = clip.OriginalDuration;
            TimeSpan start = TimeSpan.FromSeconds(segment.Start);
            TimeSpan end = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(segment.End).Ticks, length.Ticks));
            if (start < TimeSpan.Zero || end <= start)
            {
                throw new ArgumentException("Every part to keep must start before it ends, inside the video.", nameof(segments));
            }

            clip.TrimTimeFromStart = start;
            clip.TrimTimeFromEnd = length - end;
            clip.Volume = segment.Muted ? 0 : 1;
            composition.Clips.Add(clip);
        }

        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(target))!);
        StorageFile output = await folder.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.ReplaceExisting);
        // The same picture size, frame rate and sound settings as the recording, so nothing but the content changes.
        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(file);

        var operation = composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile);
        operation.Progress = (_, percent) => progress?.Report(percent / 100.0);
        TranscodeFailureReason failure = await operation;
        if (failure != TranscodeFailureReason.None)
        {
            throw new InvalidOperationException($"Windows no pudo guardar el video ({failure}).");
        }
    }
}
