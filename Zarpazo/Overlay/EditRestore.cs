using Zarpazo.History;

namespace Zarpazo.Overlay;

/// <summary>An earlier capture being edited again: what it had on it, where it now sits on the screen, and which history entry it replaces.</summary>
/// <param name="Left">Where the capture was placed, in absolute screen pixels. It can differ from <c>State.Left</c> if the screens changed.</param>
internal sealed record EditRestore(EditState State, int Left, int Top, string ReplacesPath);
