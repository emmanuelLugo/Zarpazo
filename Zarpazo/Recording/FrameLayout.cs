using System.Windows;

namespace Zarpazo.Recording;

/// <summary>Where the recording sits inside its frame. The scene is as big as the video, and the recording keeps its proportions.</summary>
/// <param name="Width">The size of the whole scene, the same as the video's.</param>
/// <param name="Content">The recording's place in the scene, in whole pixels.</param>
/// <param name="RadiusPixels">How round its corners are, in pixels of the scene.</param>
internal readonly record struct FrameLayout(int Width, int Height, Int32Rect Content, double RadiusPixels)
{
    public static FrameLayout For(VideoFrameStyle style, int width, int height)
    {
        int room = (int)Math.Round(style.Padding * height);
        double scale = Math.Min((width - 2.0 * room) / width, (height - 2.0 * room) / height);
        int contentWidth = Math.Max(2, (int)Math.Round(width * scale));
        int contentHeight = Math.Max(2, (int)Math.Round(height * scale));
        var content = new Int32Rect((width - contentWidth) / 2, (height - contentHeight) / 2, contentWidth, contentHeight);
        return new FrameLayout(width, height, content, style.Radius * height);
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
