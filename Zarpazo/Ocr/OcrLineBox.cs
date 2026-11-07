namespace Zarpazo.Ocr;

/// <summary>A line of recognized text and where it sits in the image, in image pixels.</summary>
internal readonly record struct OcrLineBox(string Text, double Left, double Top, double Width, double Height);
