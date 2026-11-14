namespace Zarpazo.Recording;

/// <summary>The title bar drawn above the recording in its frame, so it looks like a window.</summary>
internal enum WindowBar
{
    None,

    /// <summary>Three round buttons on the left, as macOS has them.</summary>
    Mac,

    /// <summary>Minimise, maximise and close on the right, as Windows has them.</summary>
    Windows,
}
