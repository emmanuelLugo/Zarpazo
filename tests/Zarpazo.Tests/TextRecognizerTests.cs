using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Ocr;

namespace Zarpazo.Tests;

public sealed class TextRecognizerTests
{
    private static BitmapSource Render(string text, double fontSize, string font = "Segoe UI", int width = 400, int height = 80)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(font), fontSize, Brushes.Black, 1.0);
            context.DrawText(formatted, new Point(10, 10));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public async Task Reads_the_text_drawn_in_an_image()
    {
        if (!TextRecognizer.IsAvailable)
        {
            return; // this machine has no language with text recognition installed
        }

        BitmapSource image = null!;
        Sta.Run(() => image = Render("Hello Zarpazo", 28));
        string text = await TextRecognizer.RecognizeAsync(image);

        Assert.Contains("Zarpazo", text);
    }

    [Fact]
    public async Task Keeps_the_indentation_of_monospaced_text()
    {
        if (!TextRecognizer.IsAvailable)
        {
            return;
        }

        BitmapSource image = null!;
        Sta.Run(() => image = Render("Shopping list\n    apples and pears\n    bread and butter\nThank you", 14, "Consolas", 300, 120));
        string[] lines = (await TextRecognizer.RecognizeAsync(image)).Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("Shopping", lines[0]);
        Assert.StartsWith("    ", lines[1]); // four spaces, as drawn
        Assert.DoesNotMatch(@"^\s{5}", lines[1]);
        Assert.StartsWith("    ", lines[2]);
        Assert.StartsWith("Thank", lines[3]);
    }

    [Fact]
    public async Task An_image_without_text_gives_an_empty_result()
    {
        if (!TextRecognizer.IsAvailable)
        {
            return;
        }

        BitmapSource image = null!;
        Sta.Run(() => image = Render("", 28));
        string text = await TextRecognizer.RecognizeAsync(image);

        Assert.Equal("", text);
    }
}
