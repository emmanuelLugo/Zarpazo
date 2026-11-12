using Xunit;

namespace Zarpazo.Tests;

/// <summary>
/// A test that needs the pictures Windows makes of a video (MediaComposition.GetThumbnailAsync), which come from the graphics
/// card. GitHub's build machines have none: there the pictures never arrive and the test hangs, so it shows up as skipped.
/// </summary>
public sealed class NeedsGpuFactAttribute : FactAttribute
{
    public NeedsGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Skip = "Video thumbnails need a GPU, and GitHub runners have none: the pictures never arrive.";
        }
    }
}
