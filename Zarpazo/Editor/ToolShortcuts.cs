using System.Windows.Input;

namespace Zarpazo.Editor;

/// <summary>The letter that picks each tool while the editor bars are open.</summary>
internal static class ToolShortcuts
{
    private static readonly IReadOnlyDictionary<Key, AnnotationTool> Tools = new Dictionary<Key, AnnotationTool>
    {
        [Key.S] = AnnotationTool.Select,
        [Key.V] = AnnotationTool.Move,
        [Key.R] = AnnotationTool.Rectangle,
        [Key.E] = AnnotationTool.Ellipse,
        [Key.L] = AnnotationTool.Line,
        [Key.A] = AnnotationTool.Arrow,
        [Key.P] = AnnotationTool.Pen,
        [Key.H] = AnnotationTool.Highlighter,
        [Key.T] = AnnotationTool.Text,
        [Key.N] = AnnotationTool.Step,
        [Key.B] = AnnotationTool.Pixelate,
        [Key.M] = AnnotationTool.Ruler,
        [Key.I] = AnnotationTool.Picker,
    };

    public static AnnotationTool? ForKey(Key key) => Tools.TryGetValue(key, out AnnotationTool tool) ? tool : null;

    /// <summary>The letter for a tool, to show in its tooltip.</summary>
    public static string KeyFor(AnnotationTool tool) => Tools.First(pair => pair.Value == tool).Key.ToString();

    /// <summary>"Rectángulo (R)": a tooltip with the letter at the end.</summary>
    public static string WithKey(string text, AnnotationTool tool) => $"{text} ({KeyFor(tool)})";
}
