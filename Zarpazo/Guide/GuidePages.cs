using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Guide;

/// <summary>
/// Lays the guide out on A4 pages and draws each one as a picture. A step is kept whole on its page when it fits; one taller
/// than a page is shrunk to fit. Drawing the text ourselves means no printer or Office is needed, and the wrapping is exact.
/// </summary>
internal static class GuidePages
{
    private const double PageWidth = 794; // A4 at 96 dpi
    private const double PageHeight = 1123;
    private const double Margin = 56;
    private const double Scale = 2; // pixels per unit: sharp enough to read small text when zoomed
    private const int JpegQuality = 90;
    private const double AfterTitle = 24;
    private const double AfterStep = 28;

    private static readonly Typeface Regular = new("Segoe UI");
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Brush Ink = Frozen(Color.FromRgb(0x20, 0x21, 0x24));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0xD9, 0x44, 0x41));
    private static readonly Pen Outline = FrozenPen(Color.FromRgb(0xDA, 0xDC, 0xE0));

    // Frozen, so they are not tied to the thread that happened to use them first.
    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(Frozen(color), 1);
        pen.Freeze();
        return pen;
    }

    /// <summary>One JPEG per page, with its size in pixels.</summary>
    public static List<(byte[] Jpeg, int Width, int Height)> Render(string title, IReadOnlyList<GuideStep> steps)
    {
        var pages = new List<(byte[], int, int)>();
        DrawingVisual? visual = null;
        DrawingContext? page = null;
        double y = 0;
        double contentWidth = PageWidth - 2 * Margin;
        double bottom = PageHeight - Margin;

        void Flush()
        {
            if (page == null)
            {
                return;
            }

            page.Close();
            int width = (int)(PageWidth * Scale);
            int height = (int)(PageHeight * Scale);
            var bitmap = new RenderTargetBitmap(width, height, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bitmap.Render(visual!);
            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            pages.Add((stream.ToArray(), width, height));
            page = null;
        }

        void NewPage()
        {
            Flush();
            visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
            page = visual.RenderOpen();
            page.DrawRectangle(Brushes.White, null, new Rect(0, 0, PageWidth, PageHeight));
            y = Margin;
        }

        NewPage();
        FormattedText heading = Text(title.Trim().Length > 0 ? title.Trim() : "Guía paso a paso", 26, Bold, Ink, contentWidth);
        page!.DrawText(heading, new Point(Margin, y));
        y += heading.Height + AfterTitle;

        foreach (GuideStep step in steps)
        {
            FormattedText label = Text(step.Heading, 15, Bold, Accent, contentWidth);
            FormattedText? body = step.Text.Trim().Length > 0 ? Text(step.Text.Trim(), 13, Regular, Ink, contentWidth) : null;
            double textHeight = label.Height + 4 + (body is null ? 0 : body.Height + 8);

            BitmapImage image = step.LoadImage();
            double width = Math.Min(image.PixelWidth, contentWidth); // never enlarged: a small capture stays crisp
            double height = width * image.PixelHeight / image.PixelWidth;
            double room = Math.Max(bottom - Margin - textHeight, 100); // what a whole page leaves for the picture
            if (height > room)
            {
                height = room;
                width = height * image.PixelWidth / image.PixelHeight;
            }

            if (y + textHeight + height > bottom && y > Margin)
            {
                NewPage();
            }

            page!.DrawText(label, new Point(Margin, y));
            y += label.Height + 4;
            if (body != null)
            {
                page.DrawText(body, new Point(Margin, y));
                y += body.Height + 8;
            }
            var rect = new Rect(Margin, y, width, height);
            page.DrawImage(image, rect);
            page.DrawRectangle(null, Outline, rect);
            y += height + AfterStep;
        }

        Flush();
        return pages;
    }

    private static FormattedText Text(string text, double size, Typeface face, Brush brush, double maxWidth) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, 1.0) { MaxTextWidth = maxWidth };
}
