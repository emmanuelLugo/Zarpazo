using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class AnnotationDataTests
{
    private const double PixelsPerUnit = 2;

    // The overlay in layout units, the screen in pixels: twice as many, starting at (100, 50).
    private static Point ToPixels(Point p) => new(100 + p.X * PixelsPerUnit, 50 + p.Y * PixelsPerUnit);

    private static Point ToLayout(Point p) => new((p.X - 100) / PixelsPerUnit, (p.Y - 50) / PixelsPerUnit);

    private static readonly ScreenSource Screen = new(
        BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[4], 4), PixelsPerUnit, PixelsPerUnit);

    private static Annotation Drag(Annotation annotation, double x, double y)
    {
        annotation.Extend(new Point(x, y));
        return annotation;
    }

    private static Annotation RoundTrip(Annotation original)
    {
        // Through JSON as well, since that is how it is stored.
        AnnotationData data = AnnotationData.From(original, ToPixels, PixelsPerUnit);
        data = JsonSerializer.Deserialize<AnnotationData>(JsonSerializer.Serialize(data))!;
        return data.ToAnnotation(ToLayout, PixelsPerUnit, Screen);
    }

    private static void AssertSame(Annotation expected, Annotation actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal(expected.Thickness, actual.Thickness, 6);
        Assert.Equal(expected.Bounds.X, actual.Bounds.X, 6);
        Assert.Equal(expected.Bounds.Y, actual.Bounds.Y, 6);
        Assert.Equal(expected.Bounds.Width, actual.Bounds.Width, 6);
        Assert.Equal(expected.Bounds.Height, actual.Bounds.Height, 6);
    }

    [Fact]
    public void Every_shape_comes_back_the_same_after_saving_and_loading()
    {
        Sta.Run(() =>
        {
            Color color = Color.FromRgb(0x12, 0xAB, 0xEF);
            var pen = new PenAnnotation(color, 3, new Point(10, 10));
            pen.Extend(new Point(40, 25));
            pen.Extend(new Point(70, 12));

            Annotation[] originals =
            [
                Drag(new RectangleAnnotation(color, 4, new Point(20, 30)), 120, 90),
                Drag(new EllipseAnnotation(color, 4, new Point(20, 30)), 120, 90),
                Drag(new LineAnnotation(color, 2, new Point(5, 5)), 80, 60),
                Drag(new ArrowAnnotation(color, 2, new Point(5, 5)), 80, 60),
                Drag(new HighlighterAnnotation(color, 4, new Point(20, 30)), 120, 90),
                Drag(new RulerAnnotation(color, 2, new Point(5, 5), Screen), 80, 60),
                pen,
                new TextAnnotation(color, 4, new Point(33, 44), "Hola\nmundo"),
                new NumberAnnotation(color, 4, new Point(60, 60), 7),
            ];

            foreach (Annotation original in originals)
            {
                AssertSame(original, RoundTrip(original));
            }
        });
    }

    [Fact]
    public void Text_and_step_number_are_kept()
    {
        Sta.Run(() =>
        {
            var text = (TextAnnotation)RoundTrip(new TextAnnotation(Colors.Red, 4, new Point(1, 2), "Hola"));
            var step = (NumberAnnotation)RoundTrip(new NumberAnnotation(Colors.Red, 4, new Point(1, 2), 7));

            Assert.Equal("Hola", text.Text);
            Assert.Equal(7, step.Number);
        });
    }

    [Fact]
    public void Positions_are_stored_as_screen_pixels_whatever_the_layout_scale()
    {
        Sta.Run(() =>
        {
            AnnotationData data = AnnotationData.From(Drag(new LineAnnotation(Colors.Red, 3, new Point(10, 20)), 30, 40), ToPixels, PixelsPerUnit);

            Assert.Equal(new double[] { 120, 90, 160, 130 }, data.Points);
            Assert.Equal(6, data.Thickness);
            Assert.Equal("#FF0000", data.Color);
        });
    }

    [Fact]
    public void Pixelate_is_not_restorable_so_the_hidden_pixels_are_not_kept()
    {
        Sta.Run(() =>
        {
            var pixelate = new PixelateAnnotation(Colors.Red, 4, new Point(0, 0), Screen);

            Assert.False(AnnotationData.CanRestore(pixelate));
            Assert.Throws<NotSupportedException>(() => AnnotationData.From(pixelate, ToPixels, PixelsPerUnit));
        });
    }

    [Theory]
    [InlineData("Nope", "#FF0000", 4)]   // unknown tool
    [InlineData("Select", "#FF0000", 4)] // a tool that doesn't draw
    [InlineData("Line", "not a color", 4)]
    [InlineData("Line", "#FF0000", 3)]   // odd number of coordinates
    [InlineData("Line", "#FF0000", 0)]   // none
    public void Damaged_data_is_rejected_instead_of_crashing(string tool, string color, int coordinates)
    {
        Sta.Run(() =>
        {
            var data = new AnnotationData { Tool = tool, Color = color, Thickness = 4, Points = new double[coordinates] };

            Assert.True(IsRejected(data));
        });

        // The overlay catches exactly these two, so anything else would escape and crash it.
        static bool IsRejected(AnnotationData data)
        {
            try
            {
                data.ToAnnotation(ToLayout, PixelsPerUnit, Screen);
                return false;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return true;
            }
        }
    }
}
