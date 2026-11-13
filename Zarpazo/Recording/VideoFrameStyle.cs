using Zarpazo.Output;

namespace Zarpazo.Recording;

/// <summary>
/// The frame a video is shown in, like Recordly and Screen Studio: the recording sits on a background, with room around it,
/// rounded corners and a shadow. The zoom moves over the whole scene, so zoomed in the recording fills the picture and the
/// background leaves it.
/// </summary>
/// <param name="Enabled">Off, the video is saved as recorded (with its zooms).</param>
/// <param name="Background">Key of one of <see cref="BeautifyBackground.All"/>, the same backgrounds as Embellecer.</param>
/// <param name="Padding">The room around the recording, as a share of the picture's height (0 to <see cref="MaxPadding"/>).</param>
/// <param name="Radius">How round the corners are, as a share of the picture's height (0 to <see cref="MaxRadius"/>).</param>
/// <param name="Shadow">How dark the shadow under the recording is: 0 none, 1 the darkest.</param>
internal sealed record VideoFrameStyle(bool Enabled, string Background, double Padding, double Radius, double Shadow)
{
    public const double MaxPadding = 0.15;
    public const double MaxRadius = 0.05;

    public static VideoFrameStyle Default { get; } = new(false, BeautifyBackground.Default.Key, 0.07, 0.012, 0.6);

    /// <summary>The same style with its values brought inside what is offered (a hand-edited settings file).</summary>
    public VideoFrameStyle Tidied() => this with
    {
        Background = BeautifyBackground.Find(Background).Key,
        Padding = Clamp(Padding, MaxPadding, Default.Padding),
        Radius = Clamp(Radius, MaxRadius, Default.Radius),
        Shadow = Clamp(Shadow, 1, Default.Shadow),
    };

    private static double Clamp(double value, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, max) : fallback;
}
