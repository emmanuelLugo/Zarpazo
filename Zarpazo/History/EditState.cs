using Zarpazo.Editor;

namespace Zarpazo.History;

/// <summary>What is needed to open a finished capture again: where it was on the screen and the annotations drawn on it.</summary>
internal sealed class EditState
{
    /// <summary>The captured area in absolute screen pixels.</summary>
    public int Left { get; set; }

    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public List<AnnotationData> Annotations { get; set; } = [];
}
