namespace Zarpazo.Recording;

/// <summary>What a <see cref="VideoOverlay"/> puts over the video.</summary>
internal enum OverlayKind
{
    /// <summary>Hides what is under a rectangle (a password, an email) behind a strong blur.</summary>
    Blur,

    /// <summary>A line of words.</summary>
    Text,

    /// <summary>A line with a head, pointing at something.</summary>
    Arrow,
}
