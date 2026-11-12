using Zarpazo.Output;

namespace Zarpazo.Recording;

/// <summary>
/// The frame a video is shown in, like Recordly and Screen Studio: the recording sits on a background, with room around it,
/// rounded corners and a shadow. The zoom moves over the whole scene, so zoomed in the recording fills the picture and the
/// background leaves it.
/// </summary>
/// <param name="Enabled">Off, the video is saved as recorded (with its zooms).</param>
/// <param name="Background">Key of one of <see cref="BeautifyBackground.All"/>, the same backgrounds as Embellecer.</param>
/// <param name="Padding">The room around the recording, as a share of the picture's height.</param>
/// <param name="Radius">How round the corners are, as a share of the picture's height.</param>
internal sealed record VideoFrameStyle(bool Enabled, string Background, double Padding, double Radius, bool Shadow)
{
    /// <summary>The choices the editor offers: small, medium and large room; square, soft and round corners.</summary>
    public static readonly double[] PaddingChoices = [0.04, 0.07, 0.11];
    public static readonly double[] RadiusChoices = [0, 0.012, 0.025];

    public static VideoFrameStyle Default { get; } = new(false, BeautifyBackground.Default.Key, 0.07, 0.012, true);

    /// <summary>The same style with its values snapped to the choices on offer (a hand-edited settings file).</summary>
    public VideoFrameStyle Tidied() => this with
    {
        Background = BeautifyBackground.Find(Background).Key,
        Padding = PaddingChoices.MinBy(choice => Math.Abs(choice - Padding)),
        Radius = RadiusChoices.MinBy(choice => Math.Abs(choice - Radius)),
    };
}
