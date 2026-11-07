using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

/// <summary>
/// An annotation as plain values, for saving it next to a capture. Points and thickness are in physical screen pixels,
/// so they mean the same thing whatever the overlay's scaling is when the capture is opened again.
/// </summary>
internal sealed class AnnotationData
{
    public string Tool { get; set; } = "";
    public string Color { get; set; } = "";
    public double Thickness { get; set; }

    /// <summary>x0, y0, x1, y1, ...: the start and end for shapes, every point for the pen, one point for text and steps.</summary>
    public double[] Points { get; set; } = [];

    public string Text { get; set; } = "";
    public int Number { get; set; }

    /// <summary>Pixelate is left out: restoring it would need the sensitive pixels it hides to be kept next to the capture.</summary>
    public static bool CanRestore(Annotation annotation) => annotation is not PixelateAnnotation;

    /// <param name="toPixels">Turns an overlay point into an absolute screen pixel.</param>
    /// <param name="pixelsPerUnit">Image pixels per overlay layout unit, to turn the thickness into pixels.</param>
    public static AnnotationData From(Annotation annotation, Func<Point, Point> toPixels, double pixelsPerUnit)
    {
        var data = new AnnotationData
        {
            Color = $"#{annotation.Color.R:X2}{annotation.Color.G:X2}{annotation.Color.B:X2}",
            Thickness = annotation.Thickness * pixelsPerUnit,
        };

        switch (annotation)
        {
            case PenAnnotation pen:
                data.Tool = nameof(AnnotationTool.Pen);
                data.Points = Flatten(pen.Points.Select(toPixels));
                break;
            case TextAnnotation text:
                data.Tool = nameof(AnnotationTool.Text);
                data.Points = Flatten([toPixels(text.Origin)]);
                data.Text = text.Text;
                break;
            case NumberAnnotation badge:
                data.Tool = nameof(AnnotationTool.Step);
                data.Points = Flatten([toPixels(badge.Center)]);
                data.Number = badge.Number;
                break;
            case DragAnnotation drag when CanRestore(drag):
                // Arrow and ruler are lines, so they have to be told apart first.
                data.Tool = drag switch
                {
                    ArrowAnnotation => nameof(AnnotationTool.Arrow),
                    RulerAnnotation => nameof(AnnotationTool.Ruler),
                    LineAnnotation => nameof(AnnotationTool.Line),
                    RectangleAnnotation => nameof(AnnotationTool.Rectangle),
                    EllipseAnnotation => nameof(AnnotationTool.Ellipse),
                    HighlighterAnnotation => nameof(AnnotationTool.Highlighter),
                    _ => throw new NotSupportedException($"{drag.GetType().Name} cannot be saved."),
                };
                data.Points = Flatten([toPixels(drag.From), toPixels(drag.To)]);
                break;
            default:
                throw new NotSupportedException($"{annotation.GetType().Name} cannot be saved.");
        }
        return data;
    }

    /// <param name="toLayout">Turns an absolute screen pixel into an overlay point.</param>
    /// <exception cref="FormatException">The saved values are not usable.</exception>
    /// <exception cref="ArgumentException">The saved tool does not draw.</exception>
    public Annotation ToAnnotation(Func<Point, Point> toLayout, double pixelsPerUnit, ScreenSource screen)
    {
        if (!Enum.TryParse(Tool, out AnnotationTool tool) || !Enum.IsDefined(tool))
        {
            throw new FormatException($"Unknown tool '{Tool}'.");
        }
        if (ColorConverter.ConvertFromString(Color) is not System.Windows.Media.Color color) // "Color" alone is this class's property
        {
            throw new FormatException($"Unknown color '{Color}'.");
        }
        if (Points.Length < 2 || Points.Length % 2 != 0)
        {
            throw new FormatException("The points are missing or incomplete.");
        }

        double thickness = Thickness / pixelsPerUnit;
        Point[] points = Enumerable.Range(0, Points.Length / 2)
            .Select(i => toLayout(new Point(Points[2 * i], Points[2 * i + 1])))
            .ToArray();

        switch (tool)
        {
            case AnnotationTool.Text:
                return new TextAnnotation(color, thickness, points[0], Text);
            case AnnotationTool.Step:
                return new NumberAnnotation(color, thickness, points[0], Number);
            case AnnotationTool.Pen:
                Annotation pen = Annotation.Create(tool, color, thickness, points[0], screen);
                foreach (Point point in points.Skip(1))
                {
                    pen.Extend(point);
                }
                return pen;
            default:
                Annotation shape = Annotation.Create(tool, color, thickness, points[0], screen);
                shape.Extend(points[^1]);
                return shape;
        }
    }

    private static double[] Flatten(IEnumerable<Point> points) => points.SelectMany(p => new[] { p.X, p.Y }).ToArray();
}
