namespace Zarpazo.Recording;

/// <summary>A click being shown: where, in shares of the recorded picture, and how far along its effect is (0 to 1).</summary>
/// <param name="Secondary">Made with the right button: its effect carries a second ring.</param>
internal readonly record struct ClickMark(double X, double Y, double Progress, bool Secondary = false);
