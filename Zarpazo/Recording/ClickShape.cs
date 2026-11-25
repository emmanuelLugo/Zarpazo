namespace Zarpazo.Recording;

/// <summary>One circle of the effects of the mouse, round a click or the pointer, in units of <see cref="CursorArt"/>.</summary>
/// <param name="Width">The width of its line, centred on the circle; 0 for a filled spot.</param>
/// <param name="Opacity">0 to 1.</param>
internal readonly record struct ClickShape(double Radius, double Width, double Opacity);
