namespace Zarpazo.Recording;

/// <summary>
/// How the pointer is drawn on an edited video, like Recordly: videos are recorded without it, and the editor draws its own
/// from the recorded track, bigger, gliding instead of jumping, leaning as it moves and reacting to clicks.
/// </summary>
/// <param name="Visible">Off, the video has no pointer at all.</param>
/// <param name="Size">How big, times the pointer on screen (<see cref="MinSize"/> to <see cref="MaxSize"/>).</param>
/// <param name="Smoothing">0 follows the hand exactly; 1 glides the most.</param>
/// <param name="Sway">0 keeps the arrow upright; 1 leans it the most as it moves sideways.</param>
/// <param name="Bounce">The pointer shrinks a moment when it clicks, as if pressed.</param>
internal sealed record CursorStyle(bool Visible, CursorShape Shape, double Size, double Smoothing, double Sway, ClickEffect Click, bool Bounce)
{
    public const double MinSize = 1;
    public const double MaxSize = 3;

    public static CursorStyle Default { get; } = new(true, CursorShape.DarkArrow, 2, 0.5, 0.3, ClickEffect.Ripple, true);

    /// <summary>The same style with its values brought inside what is offered (a hand-edited settings file).</summary>
    public CursorStyle Tidied() => this with
    {
        Shape = Enum.IsDefined(Shape) ? Shape : Default.Shape,
        Size = Math.Round(Math.Clamp(double.IsFinite(Size) ? Size : Default.Size, MinSize, MaxSize), 2),
        Smoothing = Math.Clamp(double.IsFinite(Smoothing) ? Smoothing : Default.Smoothing, 0, 1),
        Sway = Math.Clamp(double.IsFinite(Sway) ? Sway : Default.Sway, 0, 1),
        Click = Enum.IsDefined(Click) ? Click : Default.Click,
    };
}
