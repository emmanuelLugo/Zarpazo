using System.IO;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Zarpazo.Recording;

/// <summary>Cuts a video down to the part between two times, with the editing engine that comes with Windows.</summary>
internal static class VideoTrimmer
{
    /// <summary>Writes <paramref name="target"/> with only the part of <paramref name="source"/> between the two times, keeping the sound.</summary>
    /// <param name="progress">Reports 0 to 1 while it works.</param>
    /// <exception cref="InvalidOperationException">Windows could not make the video.</exception>
    public static async Task TrimAsync(string source, string target, TimeSpan start, TimeSpan end, IProgress<double>? progress = null)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(source);
        MediaClip clip = await MediaClip.CreateFromFileAsync(file);
        TimeSpan length = clip.OriginalDuration;
        end = TimeSpan.FromTicks(Math.Min(end.Ticks, length.Ticks));
        if (start < TimeSpan.Zero || end <= start)
        {
            throw new ArgumentException("The part to keep must start before it ends, inside the video.");
        }

        clip.TrimTimeFromStart = start;
        clip.TrimTimeFromEnd = length - end;
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(target))!);
        StorageFile output = await folder.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.ReplaceExisting);
        // The same picture size, frame rate and sound settings as the recording, so nothing but the length changes.
        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(file);

        var operation = composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile);
        operation.Progress = (_, percent) => progress?.Report(percent / 100.0);
        TranscodeFailureReason failure = await operation;
        if (failure != TranscodeFailureReason.None)
        {
            throw new InvalidOperationException($"Windows no pudo recortar el video ({failure}).");
        }
    }
}
