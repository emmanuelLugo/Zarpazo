namespace Zarpazo.Recording;

/// <summary>What lies under the pointer on the timeline.</summary>
internal enum TimelinePart
{
    None,

    /// <summary>The ruler (or the space above the pieces): dragging here moves the playhead.</summary>
    Ruler,

    /// <summary>The picture strip of a piece: dragging moves the playhead.</summary>
    Body,

    /// <summary>The title strip of a piece: dragging changes its place in the order.</summary>
    Grip,

    /// <summary>The left edge of a piece: dragging changes where it starts.</summary>
    StartEdge,

    /// <summary>The right edge of a piece: dragging changes where it ends.</summary>
    EndEdge,
}
