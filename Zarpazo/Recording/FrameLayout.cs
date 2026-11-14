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
    /// <summary>A scene of the same shape as the recording: the frame only shrinks it to leave room around.</summary>
    public static FrameLayout For(VideoFrameStyle style, int width, int height) => For(style, width, height, width, height);

    /// <param name="width">The scene's size, which is the video's.</param>
    /// <param name="contentWidth">The size of the recording as it is, cropped; it is fitted inside the scene, with the room the style leaves.</param>
    public static FrameLayout For(VideoFrameStyle style, int width, int height, int contentWidth, int contentHeight)
    {
        int room = (int)Math.Round(style.Padding * height);
        double scale = Math.Min((width - 2.0 * room) / contentWidth, (height - 2.0 * room) / contentHeight);
        int fittedWidth = Math.Max(2, (int)Math.Round(contentWidth * scale));
        int fittedHeight = Math.Max(2, (int)Math.Round(contentHeight * scale));
        var content = new Int32Rect((width - fittedWidth) / 2, (height - fittedHeight) / 2, fittedWidth, fittedHeight);
        return new FrameLayout(width, height, content, style.Radius * height, fittedWidth / (double)contentWidth);
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
