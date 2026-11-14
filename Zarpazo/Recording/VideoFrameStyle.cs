using Zarpazo.Output;

namespace Zarpazo.Recording;

/// <summary>
/// The frame a video is shown in, like Recordly and Screen Studio: the recording sits on a background, with room around it,
/// rounded corners and a shadow. The zoom moves over the whole scene, so zoomed in the recording fills the picture and the
/// background leaves it.
/// </summary>
/// <param name="Enabled">Off, the video is saved as recorded (with its zooms).</param>
/// <param name="Background">What is behind the recording, as <see cref="FrameBackground"/> spells it: one of Embellecer's backgrounds, a colour or gradient of one's own, or a picture.</param>
/// <param name="Padding">The room around the recording, as a share of the picture's height (0 to <see cref="MaxPadding"/>).</param>
/// <param name="Radius">How round the corners are, as a share of the picture's height (0 to <see cref="MaxRadius"/>).</param>
/// <param name="Shadow">How dark the shadow under the recording is: 0 none, 1 the darkest.</param>
internal sealed record VideoFrameStyle(bool Enabled, string Background, double Padding, double Radius, double Shadow)
{
    public const double MaxPadding = 0.15;
    public const double MaxRadius = 0.05;

    public static VideoFrameStyle Default { get; } = new(false, BeautifyBackground.Default.Key, 0.07, 0.012, 0.6);

    /// <summary>Looks to start from, one click each: a background with the room, corners and shadow that go with it.</summary>
    public static IReadOnlyList<(string Name, string Tip, VideoFrameStyle Style)> Looks { get; } =
    [
        ("Demo", "Violeta, esquinas suaves y buena sombra", Default),
        ("Limpio", "Fondo blanco, poco margen y sombra ligera", new(false, "blanco", 0.05, 0.010, 0.3)),
        ("Oscuro", "Grafito con sombra profunda, para videos de código", new(false, "grafito", 0.08, 0.018, 0.9)),
        ("Redondo", "Esquinas muy redondeadas sobre un degradado cálido", new(false, "coral", 0.09, 0.035, 0.5)),
        ("Neón", "Degradado vivo de rosa a azul con mucha sombra", new(false, FrameBackground.Custom(System.Windows.Media.Color.FromRgb(0xFF, 0x2E, 0xA6), System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0xFF), 45), 0.07, 0.020, 0.85)),
        ("Mínimo", "Casi sin margen ni sombra", new(false, "gris", 0.025, 0.006, 0.2)),
    ];

    /// <summary>
    /// The style to draw a video of another shape than its recording with: the recording sits on the background whether the
    /// frame was switched on or not, so with it off the background is there but nothing else (no room, corners or shadow).
    /// </summary>
    public VideoFrameStyle ForScene(bool letterboxed) =>
        letterboxed && !Enabled ? this with { Enabled = true, Padding = 0, Radius = 0, Shadow = 0 } : this;

    /// <summary>The same style with its values brought inside what is offered (a hand-edited settings file).</summary>
    public VideoFrameStyle Tidied() => this with
    {
        Background = FrameBackground.Tidy(Background),
        Padding = Clamp(Padding, MaxPadding, Default.Padding),
        Radius = Clamp(Radius, MaxRadius, Default.Radius),
        Shadow = Clamp(Shadow, 1, Default.Shadow),
    };

    private static double Clamp(double value, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, max) : fallback;
}
