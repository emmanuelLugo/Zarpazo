using System.Windows;

namespace Zarpazo.Recording;

/// <summary>
/// A crop laid on a picture of a given size: the rectangle kept, in whole pixels and with an even width and height (H.264
/// needs them so), which is also the finished video's size.
/// <para>
/// Where the coordinates change: the zooms (<see cref="ZoomRegion"/>) and the pointer (<see cref="CursorTrack"/>) are always
/// kept in shares of the ORIGINAL recording, so changing the crop never moves what they point at. They are turned into shares
/// of the cropped picture only when a picture is made, here and nowhere else: <see cref="ToCrop(IReadOnlyList{ZoomRegion})"/>
/// before the zooms reach the camera (in <see cref="VideoExporter"/> for the saved video and in the editor's preview camera),
/// and <see cref="ToCrop(double, double)"/> for every pose of the pointer and every click (<see cref="CursorLayer"/> and the
/// editor's pointer layer). The frame (<see cref="FrameLayout"/>) comes after: it places the cropped picture in the scene.
/// </para>
/// </summary>
/// <param name="SourceWidth">The size of the recorded picture.</param>
/// <param name="Pixels">The rectangle of it that is kept.</param>
internal readonly record struct CropArea(int SourceWidth, int SourceHeight, Int32Rect Pixels)
{
    public static CropArea For(VideoCrop crop, int width, int height)
    {
        crop = crop.Tidied();
        (int left, int kept) = Span(crop.Left, crop.Right, width);
        (int top, int keptDown) = Span(crop.Top, crop.Bottom, height);
        return new CropArea(width, height, new Int32Rect(left, top, kept, keptDown));
    }

    // From the first pixel kept, an even number of pixels that stays inside the picture.
    private static (int Start, int Length) Span(double before, double after, int size)
    {
        if (size < 2)
        {
            return (0, Math.Max(0, size));
        }
        int start = (int)Math.Round(before * size);
        int end = size - (int)Math.Round(after * size);
        int length = Math.Max(2, (end - start) & ~1);
        start = Math.Clamp(start, 0, size - length);
        return (start, length);
    }

    public int Width => Pixels.Width;

    public int Height => Pixels.Height;

    /// <summary>Nothing is cut: the picture as it was recorded.</summary>
    public bool IsWhole => Pixels.X == 0 && Pixels.Y == 0 && Pixels.Width == SourceWidth && Pixels.Height == SourceHeight;

    /// <summary>A point of the recording (shares of it) as shares of the cropped picture; outside 0 to 1 when it was cut away.</summary>
    public (double X, double Y) ToCrop(double x, double y) =>
        ((x * SourceWidth - Pixels.X) / Pixels.Width, (y * SourceHeight - Pixels.Y) / Pixels.Height);

    /// <summary>A point of the cropped picture as shares of the whole recording.</summary>
    public (double X, double Y) FromCrop(double x, double y) =>
        ((Pixels.X + x * Pixels.Width) / SourceWidth, (Pixels.Y + y * Pixels.Height) / SourceHeight);

    /// <summary>Whether a point in shares of the cropped picture is in it, not in what was cut away.</summary>
    public static bool Shows(double x, double y) => x >= 0 && x <= 1 && y >= 0 && y <= 1;

    /// <summary>The zooms with the point each one looks at moved into the cropped picture; one that looked at a part cut away looks at the nearest edge.</summary>
    public IReadOnlyList<ZoomRegion> ToCrop(IReadOnlyList<ZoomRegion> zooms)
    {
        if (IsWhole)
        {
            return zooms;
        }

        var cropped = new List<ZoomRegion>(zooms.Count);
        foreach (ZoomRegion zoom in zooms)
        {
            (double x, double y) = ToCrop(zoom.FocusX, zoom.FocusY);
            cropped.Add(zoom with { FocusX = Math.Clamp(x, 0, 1), FocusY = Math.Clamp(y, 0, 1) });
        }
        return cropped;
    }
}
