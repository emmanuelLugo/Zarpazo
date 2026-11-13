namespace Zarpazo.Recording;

/// <summary>What is drawn where the mouse is clicked, in an edited video.</summary>
internal enum ClickEffect
{
    None,

    /// <summary>A ring that grows from the pointer and fades.</summary>
    Ripple,

    /// <summary>A soft round spot that swells and fades.</summary>
    Pulse,
}
