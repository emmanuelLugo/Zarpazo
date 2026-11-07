using System.IO;

namespace Zarpazo.Recording;

/// <summary>One way of recording the screen into a file. The session drives it without caring whether it is a GIF or a video.</summary>
internal abstract class RecordingJob
{
    private static readonly TimeSpan TempFileLifetime = TimeSpan.FromDays(7);

    /// <summary>What the file is called in messages: "GIF" or "video".</summary>
    public abstract string Label { get; }

    public abstract string Extension { get; }

    /// <summary>The filter of the Save dialog.</summary>
    public abstract string FileFilter { get; }

    public abstract TimeSpan MaxDuration { get; }

    public abstract TimeSpan Elapsed { get; }

    /// <summary>The sound sources and how loud each is, for the bar while recording; none for a GIF or a silent video.</summary>
    public virtual IReadOnlyList<AudioMeter> Meters => [];

    /// <summary>Raised, from a capture thread, when the job stops by itself.</summary>
    public abstract event Action<RecordingEnd>? LimitReached;

    /// <param name="folder">Where the file is made. A video is written into it while recording; a GIF when it ends.</param>
    /// <exception cref="InvalidOperationException">Something it needs is missing, such as a microphone.</exception>
    public abstract void Start(string folder);

    /// <summary>Ends the recording and completes the file; returns its path, or null if nothing was recorded.</summary>
    public abstract Task<string?> FinishAsync();

    /// <summary>Ends the recording and throws it away.</summary>
    public abstract void Abort();

    /// <summary>A new file name in the folder (created if needed), after clearing out the old files of the same kind.</summary>
    protected string NewFilePath(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string old in Directory.EnumerateFiles(folder, "*" + Extension))
        {
            try
            {
                if (DateTime.Now - File.GetLastWriteTime(old) > TempFileLifetime)
                {
                    File.Delete(old);
                }
            }
            catch (IOException)
            {
                // Still open somewhere; it goes next time.
            }
        }

        // No spaces, so a pasted path needs no quotes.
        return Path.Combine(folder, $"Zarpazo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{Extension}");
    }
}
