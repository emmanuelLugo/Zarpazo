namespace Zarpazo.Recording;

/// <summary>The sides of a crop: one, two for a corner, or all four to move the whole rectangle.</summary>
[Flags]
internal enum CropEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom,
}
