namespace Zarpazo.Editor;

/// <summary>Select means "drag a new capture area"; the rest draw inside the current one.</summary>
public enum AnnotationTool
{
    Select,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pen,
    Highlighter,
    Text,
    Step,
    Pixelate,
}
