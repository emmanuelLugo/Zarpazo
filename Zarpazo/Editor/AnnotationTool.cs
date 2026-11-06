namespace Zarpazo.Editor;

/// <summary>Select adjusts the capture area; Move adjusts the drawn annotations; the rest draw inside the area.</summary>
public enum AnnotationTool
{
    Select,
    Move,
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
