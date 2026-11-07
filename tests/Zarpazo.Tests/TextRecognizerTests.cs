using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Ocr;

namespace Zarpazo.Tests;

public sealed class TextRecognizerTests
{
    private static BitmapSource Render(string text, double fontSize)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 400, 80));
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, Brushes.Black, 1.0);
            context.DrawText(formatted, new Point(10, 10));
        }

        var bitmap = new RenderTargetBitmap(400, 80, 96, 96, PixelFormats.Pbgra32);
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
