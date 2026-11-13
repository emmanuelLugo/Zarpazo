namespace Zarpazo.Recording;

/// <summary>
/// Everything about how an edited video looks that is a matter of taste rather than of its content: the frame, the pointer
/// and the zooms' style. It is what a preset keeps and what applying one changes; the crop, the cuts and the zooms of a
/// video are not part of it.
/// </summary>
internal sealed record VideoLook(VideoFrameStyle Frame, CursorStyle Cursor, ZoomStyle Zoom)
{
    public static VideoLook Default { get; } = new(VideoFrameStyle.Default, CursorStyle.Default, ZoomStyle.Default);

    /// <summary>The same look with every part in range; a part missing (a hand-edited file) takes its default.</summary>
    public VideoLook Tidied() => new(
        (Frame ?? VideoFrameStyle.Default).Tidied(),
        (Cursor ?? CursorStyle.Default).Tidied(),
        (Zoom ?? ZoomStyle.Default).Tidied());
}
