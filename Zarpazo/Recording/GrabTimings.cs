namespace Zarpazo.Recording;

/// <summary>How long each step of taking one picture took, in milliseconds.</summary>
/// <param name="ByGraphicsCard">The screen was copied through the graphics card rather than with CopyFromScreen.</param>
internal readonly record struct GrabTimings(double InputMs, double CopyMs, double DrawMs, double ScaleMs, double OutMs, bool ByGraphicsCard);
