using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Zarpazo.Ocr;

/// <summary>Reads the text in a picture with the OCR that comes with Windows: offline, in the languages the person has installed.</summary>
internal static class TextRecognizer
{
    private const int Upscale = 2; // screen text is small; the engine reads it far better when enlarged

    private static readonly Lazy<OcrEngine?> Engine = new(OcrEngine.TryCreateFromUserProfileLanguages);

    /// <summary>False when no installed language has text recognition (Settings > Time &amp; language > Language &amp; region).</summary>
    public static bool IsAvailable => Engine.Value != null;

    /// <summary>The text in the image, one line per line of text. Empty when none was found.</summary>
    public static async Task<string> RecognizeAsync(BitmapSource image)
    {
        OcrEngine engine = Engine.Value ?? throw new InvalidOperationException("Windows no tiene un idioma con reconocimiento de texto.");

        using SoftwareBitmap bitmap = ToSoftwareBitmap(image);
        OcrResult result = await engine.RecognizeAsync(bitmap);
        return TextLayout.Format(result.Lines.Select(ToBox).Where(box => box.Text.Length > 0).ToList()).TrimEnd();
    }

    private static OcrLineBox ToBox(OcrLine line)
    {
        if (line.Words.Count == 0)
        {
            return new OcrLineBox("", 0, 0, 0, 0);
        }

        double left = line.Words.Min(word => word.BoundingRect.Left);
        double top = line.Words.Min(word => word.BoundingRect.Top);
        double right = line.Words.Max(word => word.BoundingRect.Right);
        double bottom = line.Words.Max(word => word.BoundingRect.Bottom);
        return new OcrLineBox(line.Text.Trim(), left, top, right - left, bottom - top);
    }

    private static SoftwareBitmap ToSoftwareBitmap(BitmapSource image)
    {
        BitmapSource source = image;
        int max = (int)OcrEngine.MaxImageDimension;
        if (image.PixelWidth * Upscale <= max && image.PixelHeight * Upscale <= max)
        {
            source = new TransformedBitmap(image, new ScaleTransform(Upscale, Upscale));
        }
        else if (image.PixelWidth > max || image.PixelHeight > max)
        {
            double fit = Math.Min((double)max / image.PixelWidth, (double)max / image.PixelHeight);
            source = new TransformedBitmap(image, new ScaleTransform(fit, fit));
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, converted.PixelWidth, converted.PixelHeight);
    }
}
