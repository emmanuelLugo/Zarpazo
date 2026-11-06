namespace Zarpazo.Editor;

/// <summary>Select means "drag a new capture area"; the rest draw inside the current one.</summary>
public enum AnnotationTool
{
    Select,
    Rectangle,
    Line,
    Arrow,
    Pen,
    Text,
}
