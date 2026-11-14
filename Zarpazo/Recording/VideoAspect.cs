namespace Zarpazo.Recording;

/// <summary>
/// The shape of the finished video. Anything but <see cref="Original"/> puts the recording (cropped, if it is) in the middle of
/// a canvas of that shape, on the frame's background: a video for a phone, a post or a slide from a screen recording.
/// </summary>
internal enum VideoAspect
{
    /// <summary>The recording's own shape (that of its crop).</summary>
    Original,

    /// <summary>16:9, landscape.</summary>
    Wide,

    /// <summary>9:16, portrait: stories, reels, shorts.</summary>
    Tall,

    /// <summary>1:1, a post.</summary>
    Square,
}
