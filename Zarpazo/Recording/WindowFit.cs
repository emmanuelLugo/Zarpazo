using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;

namespace Zarpazo.Recording;

/// <summary>Where a window designed at a given size goes on the screen it opens on, which need not be the main one.</summary>
internal static class WindowFit
{
    private const double ScreenShare = 0.94; // of the screen's work area, at most
    private const double Spare = 40;         // room to spare around the smallest size; with less, the window opens maximized

    /// <param name="work">The screen's work area, in physical pixels.</param>
    /// <param name="scale">That screen's scaling: 1 at 100 %, 1.5 at 150 %.</param>
    /// <param name="designed">The size the window was designed at, in WPF units.</param>
    /// <param name="smallest">The smallest size it is still usable at, in WPF units.</param>
    /// <returns>The window's place in physical pixels, centred, and whether the screen is too small for anything but maximized.</returns>
    public static (Rectangle Bounds, bool Maximize) Centered(Rectangle work, double scale, Size designed, Size smallest)
    {
        double roomWidth = work.Width / scale;
        double roomHeight = work.Height / scale;
        double width = Math.Max(smallest.Width, Math.Min(designed.Width, roomWidth * ScreenShare));
        double height = Math.Max(smallest.Height, Math.Min(designed.Height, roomHeight * ScreenShare));

        // Never past the work area, even at the smallest size: the title bar has to stay within reach.
        int pixelWidth = Math.Min(work.Width, (int)Math.Round(width * scale));
        int pixelHeight = Math.Min(work.Height, (int)Math.Round(height * scale));
        var bounds = new Rectangle(work.X + (work.Width - pixelWidth) / 2, work.Y + (work.Height - pixelHeight) / 2, pixelWidth, pixelHeight);
        bool maximize = roomWidth < smallest.Width + Spare || roomHeight < smallest.Height + Spare;
        return (bounds, maximize);
    }
}
