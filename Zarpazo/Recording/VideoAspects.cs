namespace Zarpazo.Recording;

/// <summary>What a <see cref="VideoAspect"/> means in pixels.</summary>
internal static class VideoAspects
{
    /// <summary>A video is not made bigger than this on its longest side, nor than the recording's own, whichever is larger.</summary>
    private const int FullHdSide = 1920;

    /// <summary>The shapes people can pick, in the order they are offered.</summary>
    public static readonly VideoAspect[] All = [VideoAspect.Original, VideoAspect.Wide, VideoAspect.Tall, VideoAspect.Square];

    /// <summary>Width over height of the shape; null for the recording's own.</summary>
    public static double? Ratio(this VideoAspect aspect) => aspect switch
    {
        VideoAspect.Wide => 16 / 9.0,
        VideoAspect.Tall => 9 / 16.0,
        VideoAspect.Square => 1,
        _ => null,
    };

    /// <summary>The name shown in the editor.</summary>
    public static string Label(this VideoAspect aspect) => aspect switch
    {
        VideoAspect.Wide => "16:9",
        VideoAspect.Tall => "9:16",
        VideoAspect.Square => "1:1",
        _ => "Original",
    };

    /// <summary>
    /// The size of the whole video for a cropped picture of <paramref name="width"/> by <paramref name="height"/>: the smallest
    /// canvas of the chosen shape that holds the picture at its own size, kept inside <see cref="FullHdSide"/> (or the
    /// recording's own longest side, when that is more) so a tall video from a wide recording is not enormous. Both sides even,
    /// as H.264 needs. A shape the picture already has (within a hair) leaves it as it is.
    /// </summary>
    public static (int Width, int Height) SceneSize(this VideoAspect aspect, int width, int height)
    {
        if (aspect.Ratio() is not { } ratio || height <= 0 || Math.Abs(ratio / ((double)width / height) - 1) < 0.01)
        {
            return (width, height);
        }

        double canvasWidth = width, canvasHeight = height;
        if (ratio >= (double)width / height)
        {
            canvasWidth = height * ratio;
        }
        else
        {
            canvasHeight = width / ratio;
        }

        double cap = Math.Max(FullHdSide, Math.Max(width, height));
        double longest = Math.Max(canvasWidth, canvasHeight);
        if (longest > cap)
        {
            canvasWidth *= cap / longest;
            canvasHeight *= cap / longest;
        }
        return (Even(canvasWidth), Even(canvasHeight));
    }

    /// <summary>Whether the video is a different shape than its picture, so the picture sits on a background.</summary>
    public static bool IsLetterboxed(this VideoAspect aspect, int width, int height) => aspect.SceneSize(width, height) != (width, height);

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value) & ~1);
}
