namespace Zarpazo.Overlay;

/// <summary>The part of the capture area under the mouse: inside it, or one of its eight grab points.</summary>
public enum SelectionHandle
{
    None,
    Inside,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}
