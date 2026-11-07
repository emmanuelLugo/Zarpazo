using System.Drawing;

namespace Zarpazo.Recording;

/// <summary>What the keyboard and the mouse are doing right now. Behind an interface so the effects can be tested without touching the real ones.</summary>
internal interface IInputState
{
    /// <summary>Whether the key or mouse button is held down at this moment.</summary>
    bool IsDown(int virtualKey);

    /// <summary>Whether it was pressed since the last time this was asked, even if it is already up (a very quick click).</summary>
    bool WasPressedSinceLastCheck(int virtualKey);

    /// <summary>Where the pointer is, in screen pixels.</summary>
    Point CursorPosition { get; }
}
