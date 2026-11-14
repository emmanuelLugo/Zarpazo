namespace Zarpazo.Recording;

/// <summary>
/// How the finished video is written, apart from what it shows: its size, how much data each picture gets and how many
/// pictures a second. Chosen in the editor's Exportar panel and remembered for the next video; it is not part of a look or
/// a preset, and changing it is no edit to undo. The default is what Zarpazo always wrote.
/// </summary>
/// <param name="Fps">Pictures a second; 0 keeps the recording's own rate. A rate above the recording's is not made up: the video keeps the recording's.</param>
internal sealed record VideoExportOptions(ExportResolution Resolution, ExportQuality Quality, int Fps)
{
    /// <summary>The rates offered besides "as recorded" (0).</summary>
    public static readonly int[] FpsChoices = [0, 24, 30, 60];

    public static VideoExportOptions Default { get; } = new(ExportResolution.Original, ExportQuality.Balanced, 0);

    public bool IsDefault => this == Default;

    /// <summary>The share of each side of the picture that is kept.</summary>
    public double Scale => Resolution switch
    {
        ExportResolution.High => 0.9,
        ExportResolution.Medium => 0.75,
        ExportResolution.Low => 0.5,
        _ => 1,
    };

    /// <summary>What the bitrate is multiplied by, next to the balanced one.</summary>
    public double QualityFactor => Quality switch
    {
        ExportQuality.Light => 0.6,
        ExportQuality.High => 1.6,
        _ => 1,
    };

    /// <summary>The size of the video for a picture of <paramref name="width"/> x <paramref name="height"/>; both even, as H.264 needs.</summary>
    public (int Width, int Height) SizeFor(int width, int height) =>
        Scale >= 1 ? (width, height) : (Even(width * Scale), Even(height * Scale));

    /// <summary>The same options with every value inside what is offered (a hand-edited settings file).</summary>
    public VideoExportOptions Tidied() => new(
        Enum.IsDefined(Resolution) ? Resolution : Default.Resolution,
        Enum.IsDefined(Quality) ? Quality : Default.Quality,
        Array.IndexOf(FpsChoices, Fps) >= 0 ? Fps : Default.Fps);

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value) & ~1);
}
