namespace Zarpazo.Output;

/// <summary>How a capture is dressed up: a gradient behind it, room around it, rounded corners and a shadow.</summary>
/// <param name="Background">Key of one of <see cref="BeautifyBackground.All"/>.</param>
/// <param name="Padding">Pixels of background around the capture.</param>
/// <param name="Radius">Pixels of rounding on the capture's corners.</param>
internal sealed record BeautifyStyle(string Background, int Padding, int Radius, bool Shadow)
{
    /// <summary>The options the panel offers; hand-edited settings snap to the nearest one.</summary>
    /// <summary>A title bar above the capture, so it looks like a window.</summary>
    public Recording.WindowBar Bar { get; init; } = Recording.WindowBar.None;

    /// <summary>The bar in light tones; otherwise dark ones.</summary>
    public bool LightBar { get; init; }

    public static readonly int[] PaddingChoices = [24, 48, 80];
    public static readonly int[] RadiusChoices = [0, 12, 24];

    public static BeautifyStyle Default { get; } = new(BeautifyBackground.Default.Key, 48, 12, true);

    public static int Nearest(int value, int[] choices) => choices.MinBy(choice => Math.Abs(choice - value));
}
