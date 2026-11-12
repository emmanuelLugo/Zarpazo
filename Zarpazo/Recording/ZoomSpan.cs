namespace Zarpazo.Recording;

/// <summary>A zoom placed on the edited video: from <see cref="Start"/> to <see cref="End"/> seconds of the result.</summary>
/// <param name="Index">Which of the edit's zooms it is: a cut can show one zoom in several places.</param>
internal readonly record struct ZoomSpan(double Start, double End, double Scale, double FocusX, double FocusY, int Index);
