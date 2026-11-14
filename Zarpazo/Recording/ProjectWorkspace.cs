using System.IO;
using Zarpazo.History;
using Zarpazo.Settings;

namespace Zarpazo.Recording;

/// <summary>
/// Opens a video of the history in the editor to carry on from where it was left: the clean recording and the edit that were
/// kept with it. The editor works on a copy in the temporary folder, like it does on a new recording, so the history's own
/// files are never open while they are replaced; what is copied or saved from there takes the entry's place.
/// </summary>
internal static class ProjectWorkspace
{
    /// <summary>What the editor needs to carry on: the working copy, the delivery that replaces the entry, and the saved edit.</summary>
    internal sealed record Prepared(string Work, VideoDelivery Delivery, VideoProject Project);

    /// <param name="entryPath">The history entry's picture, which the other files are named after.</param>
    /// <param name="folder">Where the working copy goes; the temporary folder Zarpazo uses by default.</param>
    /// <returns>The editor, not yet shown; null when the entry has no edit to carry on from.</returns>
    /// <exception cref="IOException">The recording could not be copied.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public static async Task<VideoEditWindow?> OpenAsync(CaptureHistory history, SettingsStore settings, string entryPath, string? folder = null)
    {
        if (await PrepareAsync(history, settings, entryPath, folder) is not { } prepared)
        {
            return null;
        }
        return new VideoEditWindow(prepared.Work, settings, prepared.Delivery, project: prepared.Project);
    }

    /// <summary>Copies the clean recording to work on; null when the entry has no edit to carry on from. Throws like <see cref="OpenAsync"/>.</summary>
    public static async Task<Prepared?> PrepareAsync(CaptureHistory history, SettingsStore settings, string entryPath, string? folder = null)
    {
        if (history.LoadProject(entryPath) is not { } found)
        {
            return null;
        }

        folder ??= Path.Combine(Path.GetTempPath(), "Zarpazo");
        Directory.CreateDirectory(folder);
        string name = Path.GetFileNameWithoutExtension(entryPath);
        string work = Path.Combine(folder, name + RecordingFiles.OriginalSuffix + ".mp4");
        await Task.Run(() =>
        {
            File.Copy(found.Source, work, overwrite: true); // a recording can be hundreds of megabytes: not on the window's thread
            CursorTrack.CopyAlong(found.Source, work);
        });

        var delivery = new VideoDelivery(work, Path.Combine(folder, name + ".mp4"), settings, history, CaptureHistory.Load(entryPath), historyPath: entryPath);
        return new Prepared(work, delivery, found.Project);
    }
}
