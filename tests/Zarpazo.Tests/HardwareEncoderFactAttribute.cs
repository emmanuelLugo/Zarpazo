using Xunit;

namespace Zarpazo.Tests;

/// <summary>
/// A test that needs the hardware H.264 encoder: the software one ignores the bitrate it is asked for.
/// GitHub's build machines have no GPU, so there it shows up as skipped instead of failing.
/// </summary>
public sealed class HardwareEncoderFactAttribute : FactAttribute
{
    public HardwareEncoderFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Skip = "The software H.264 encoder (no GPU on GitHub runners) ignores the bitrate.";
        }
    }
}
