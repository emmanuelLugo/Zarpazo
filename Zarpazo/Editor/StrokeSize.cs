namespace Zarpazo.Editor;

/// <summary>The size every drawing tool shares, changed with the mouse wheel: the stroke width, or the text and badge size.</summary>
internal static class StrokeSize
{
    public const double Min = 1;
    public const double Max = 30;
    public const double Default = 4;

    /// <summary>How much one notch of the wheel changes the size.</summary>
    public const double Step = 1;

    public static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, Min, Max) : Default;

    /// <summary>Whether the wheel sizes what this tool draws. The highlighter, pixelate and ruler have no size to change.</summary>
    public static bool UsesWheel(AnnotationTool tool) => tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Line
        or AnnotationTool.Arrow or AnnotationTool.Pen or AnnotationTool.Text or AnnotationTool.Step;

    /// <summary>The width of the circle that follows the pointer: roughly how big the next mark will be.</summary>
    public static double BubbleDiameter(AnnotationTool tool, double thickness) => tool switch
    {
        AnnotationTool.Text => TextAnnotation.SizeFor(thickness),
        AnnotationTool.Step => NumberAnnotation.RadiusFor(thickness) * 2,
        _ => thickness,
    };
}
