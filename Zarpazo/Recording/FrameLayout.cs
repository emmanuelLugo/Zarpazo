using System.Windows;

namespace Zarpazo.Recording;

/// <summary>
/// Where the recording sits inside its frame. The scene is as big as the video; the recording (cropped, if it is) keeps its
/// proportions, so when the video has another shape (see <see cref="VideoAspect"/>) it is centred with the background around it.
/// </summary>
/// <param name="Width">The size of the whole scene, the same as the video's.</param>
/// <param name="Content">The recording's place in the scene, in whole pixels.</param>
/// <param name="RadiusPixels">How round its corners are, in pixels of the scene.</param>
/// <param name="ContentScale">How many pixels of the scene one pixel of the recording (cropped) takes.</param>
internal readonly record struct FrameLayout(int Width, int Height, Int32Rect Content, double RadiusPixels, double ContentScale)
{
    /// <summary>How tall the title bar is, as a share of the scene's height.</summary>
    public const double BarShare = 0.036;

    /// <summary>The title bar's height in pixels of the scene; 0 without one. The bar sits right above <see cref="Content"/>.</summary>
    public int BarHeight { get; init; }

    /// <summary>The width of the line along the card's edge, in pixels of the scene.</summary>
    public double BorderPixels { get; init; }

    /// <summary>The whole card the frame draws: the recording and, above it, the bar. Its corners are the rounded ones.</summary>
    public Int32Rect Card => new(Content.X, Content.Y - BarHeight, Content.Width, Content.Height + BarHeight);

    /// <summary>A scene of the same shape as the recording: the frame only shrinks it to leave room around.</summary>
    public static FrameLayout For(VideoFrameStyle style, int width, int height) => For(style, width, height, width, height);

    /// <summary>
    /// The layout of a saved video: the scene (<paramref name="width"/> x <paramref name="height"/>, the recording's size or a
    /// canvas around it) grows, keeping its shape, until the recording fits in at its own size and the frame's room is added
    /// around it. Shrinking the recording to make that room blurred its text. It grows up to 4K at most.
    /// </summary>
    public static FrameLayout ForExport(VideoFrameStyle style, int width, int height, int contentWidth, int contentHeight)
    {
        FrameLayout layout = For(style, width, height, contentWidth, contentHeight);
        double limit = Math.Min(MaxExportSide / (double)Math.Max(width, height), Math.Sqrt(MaxExportPixels / ((double)width * height)));
        double grow = Math.Min(1 / layout.ContentScale, limit);
        // Rounding to whole, even pixels can leave the recording a pixel short: then the scene grows two more.
        for (int tries = 0; grow > 1 && tries < 20; tries++)
        {
            layout = For(style, Even(width * grow), Even(height * grow), contentWidth, contentHeight, maxScale: 1);
            if (layout.ContentScale >= 1 || grow >= limit)
            {
                break;
            }
            grow = Math.Min(limit, grow + 2.0 / Math.Max(width, height));
        }
        return layout;
    }

    // The biggest scene ForExport makes: 4K, which hardware H.264 encoders take.
    private const int MaxExportSide = 3840;
    private const double MaxExportPixels = 3840.0 * 2160;

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value) & ~1);

    /// <param name="width">The scene's size, which is the video's.</param>
    /// <param name="contentWidth">The size of the recording as it is, cropped; it is fitted inside the scene, with the room the style leaves (and the bar's height, if it has one).</param>
    /// <param name="maxScale">The most the recording is enlarged to fill its place; it is then centred in it.</param>
    public static FrameLayout For(VideoFrameStyle style, int width, int height, int contentWidth, int contentHeight, double maxScale = double.PositiveInfinity)
    {
        (double roomLeft, double roomTop, double roomRight, double roomBottom) = style.Room;
        int left = (int)Math.Round(roomLeft * height), top = (int)Math.Round(roomTop * height);
        int right = (int)Math.Round(roomRight * height), bottom = (int)Math.Round(roomBottom * height);
        int bar = style.Bar == WindowBar.None ? 0 : Math.Max(8, (int)Math.Round(BarShare * height)) & ~1;
        // However much room is asked for, a fifth of the scene is always left for the recording.
        double freeWidth = Math.Max(width * 0.2, width - left - right);
        double freeHeight = Math.Max(height * 0.2, height - top - bottom - bar);
        double scale = Math.Min(maxScale, Math.Min(freeWidth / contentWidth, freeHeight / contentHeight));
        int fittedWidth = Math.Max(2, (int)Math.Round(contentWidth * scale));
        int fittedHeight = Math.Max(2, (int)Math.Round(contentHeight * scale));
        // The recording (and its bar) is centred in the space the room leaves.
        int x = (int)Math.Floor(left + (width - left - right - fittedWidth) / 2.0);
        int y = (int)Math.Floor(top + (height - top - bottom - bar - fittedHeight) / 2.0) + bar;
        var content = new Int32Rect(Math.Clamp(x, 0, Math.Max(0, width - fittedWidth)), Math.Clamp(y, bar, Math.Max(bar, height - fittedHeight)), fittedWidth, fittedHeight);
        return new FrameLayout(width, height, content, style.Radius * height, fittedWidth / (double)contentWidth)
        {
            BarHeight = bar,
            BorderPixels = style.BorderWidth * height,
        };
    }

    /// <summary>A point of the recording (shares of it, 0 to 1) as shares of the whole scene.</summary>
    public (double X, double Y) ToScene(double x, double y) =>
        ((Content.X + x * Content.Width) / Width, (Content.Y + y * Content.Height) / Height);

    /// <summary>The zooms with the point each one looks at moved from the recording to the scene.</summary>
    public IReadOnlyList<ZoomRegion> ToScene(IReadOnlyList<ZoomRegion> zooms)
    {
        var scene = new List<ZoomRegion>(zooms.Count);
        foreach (ZoomRegion zoom in zooms)
        {
            (double x, double y) = ToScene(zoom.FocusX, zoom.FocusY);
            scene.Add(zoom with { FocusX = x, FocusY = y });
        }
        return scene;
    }
}
