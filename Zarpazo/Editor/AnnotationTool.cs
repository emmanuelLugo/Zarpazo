namespace Zarpazo.Editor;

/// <summary>Select adjusts the capture area; Move adjusts the drawn annotations; Picker reads colors from the screen;
/// the rest draw inside the area.</summary>
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
    Ruler,
    Picker,
}
