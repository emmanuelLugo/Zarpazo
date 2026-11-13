namespace Zarpazo.Recording;

/// <summary>What the pointer drawn on an edited video looks like.</summary>
internal enum CursorShape
{
    /// <summary>A dark arrow with a white edge, which stands out on light windows.</summary>
    DarkArrow,

    /// <summary>A white arrow with a dark edge, like Windows' own.</summary>
    LightArrow,

    /// <summary>A round dot, for when the arrow would distract.</summary>
    Dot,
}
