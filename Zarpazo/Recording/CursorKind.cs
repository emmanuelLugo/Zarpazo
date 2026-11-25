namespace Zarpazo.Recording;

/// <summary>Which of Windows' pointers was showing. Any other (the wait circle, a program's own) counts as the arrow.</summary>
internal enum CursorKind
{
    Arrow,
    Hand,
    Text,
    ResizeHorizontal,
    ResizeVertical,

    /// <summary>From the top left to the bottom right.</summary>
    ResizeDiagonalDown,

    /// <summary>From the bottom left to the top right.</summary>
    ResizeDiagonalUp,
    Move,
}
