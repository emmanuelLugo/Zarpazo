namespace Zarpazo.Recording;

internal enum VideoQuality
{
    Low,
    Medium,
    High,
}

/// <summary>How a recording is made: how often a picture is taken, how good it is, and how long it can last.</summary>
/// <param name="GifDither">Spread the colors a GIF has to give up so gradients look smooth instead of banded; the file is larger.</param>
internal sealed record RecordingQuality(
    int GifFramesPerSecond = 12,
    bool GifDither = false,
    int GifMaxSeconds = 30,
    int VideoFramesPerSecond = 30,
    VideoQuality VideoQuality = VideoQuality.Medium,
    int VideoMaxMinutes = 30)
{
    /// <summary>The values the settings window offers; whatever is in the settings file snaps to the nearest one.</summary>
    public static readonly int[] GifFramesPerSecondChoices = [8, 12, 15, 20];
    public static readonly int[] GifMaxSecondsChoices = [15, 30, 60];
    public static readonly int[] VideoFramesPerSecondChoices = [15, 24, 30];
    public static readonly int[] VideoMaxMinutesChoices = [5, 10, 30, 60];

    public static RecordingQuality Default { get; } = new();

    /// <summary>Bits spent on each pixel of each picture: the higher, the sharper (text especially) and the bigger the file.</summary>
    public double BitsPerPixelPerFrame => VideoQuality switch
    {
        VideoQuality.Low => 0.07,
        VideoQuality.High => 0.20,
        _ => 0.12,
    };

    public static int Nearest(int value, int[] choices) => choices.MinBy(choice => Math.Abs(choice - value));
}
