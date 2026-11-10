using System.Windows;

namespace Zarpazo.Overlay;

internal static class SelectionGeometry
{
    /// <summary>
    /// A rectangle in physical screen pixels as an area of the overlay, cut to the screens that are there now. Empty when nothing
    /// of it is left (a remembered area on a monitor that has since been unplugged).
    /// </summary>
    public static Rect FromScreenPixels(System.Drawing.Rectangle pixels, System.Drawing.Rectangle bounds, double scaleX, double scaleY)
    {
        System.Drawing.Rectangle visible = System.Drawing.Rectangle.Intersect(pixels, bounds);
        if (visible.Width < 2 || visible.Height < 2)
        {
            return Rect.Empty;
        }

        return new Rect((visible.X - bounds.X) / scaleX, (visible.Y - bounds.Y) / scaleY, visible.Width / scaleX, visible.Height / scaleY);
    }

    /// <summary>
    /// Whether a window covers (nearly) a whole monitor, as a maximized one does. Highlighting it would leave the overlay looking
    /// undimmed, so the overlay starts dark and the area is chosen by dragging.
    /// </summary>
    public static bool FillsAMonitor(System.Drawing.Rectangle window, IEnumerable<System.Drawing.Rectangle> monitors)
    {
        foreach (System.Drawing.Rectangle monitor in monitors)
        {
            System.Drawing.Rectangle shared = System.Drawing.Rectangle.Intersect(window, monitor);
            if ((long)shared.Width * shared.Height >= 0.9 * monitor.Width * monitor.Height)
            {
                return true; // 90%: the taskbar keeps a maximized window from covering all of it
            }
        }
        return false;
    }

    /// <summary>The area can't be shrunk below this, so there is always something left to grab.</summary>
    public const double MinSize = 8;

    /// <summary>Which part of the area the point touches. Corners win over edges, and edges over the inside.</summary>
    public static SelectionHandle HitTest(Rect area, Point point, double tolerance)
    {
        bool nearLeft = Math.Abs(point.X - area.Left) <= tolerance;
        bool nearRight = Math.Abs(point.X - area.Right) <= tolerance;
        bool nearTop = Math.Abs(point.Y - area.Top) <= tolerance;
        bool nearBottom = Math.Abs(point.Y - area.Bottom) <= tolerance;

        // The grab zone also reaches a little outside the area, so the edges are easy to catch.
        bool withinX = point.X >= area.Left - tolerance && point.X <= area.Right + tolerance;
        bool withinY = point.Y >= area.Top - tolerance && point.Y <= area.Bottom + tolerance;
        if (!withinX || !withinY)
        {
            return SelectionHandle.None;
        }

        if (nearTop && nearLeft) return SelectionHandle.NorthWest;
        if (nearTop && nearRight) return SelectionHandle.NorthEast;
        if (nearBottom && nearLeft) return SelectionHandle.SouthWest;
        if (nearBottom && nearRight) return SelectionHandle.SouthEast;
        if (nearTop) return SelectionHandle.North;
        if (nearBottom) return SelectionHandle.South;
        if (nearLeft) return SelectionHandle.West;
        if (nearRight) return SelectionHandle.East;
        return area.Contains(point) ? SelectionHandle.Inside : SelectionHandle.None;
    }

    /// <summary>
    /// The box of an annotation after dragging a grip by <paramref name="delta"/>. Unlike the capture area it may get flat (a line is),
    /// but never turns inside out, and it stays inside <paramref name="bounds"/> unless it started outside.
    /// </summary>
    public static Rect Stretch(Rect original, SelectionHandle handle, Vector delta, Rect bounds)
    {
        bounds = Rect.Union(bounds, original);
        double left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
        if (handle is SelectionHandle.West or SelectionHandle.NorthWest or SelectionHandle.SouthWest)
        {
            left = Math.Clamp(original.Left + delta.X, bounds.Left, original.Right);
        }
        if (handle is SelectionHandle.East or SelectionHandle.NorthEast or SelectionHandle.SouthEast)
        {
            right = Math.Clamp(original.Right + delta.X, original.Left, bounds.Right);
        }
        if (handle is SelectionHandle.North or SelectionHandle.NorthWest or SelectionHandle.NorthEast)
        {
            top = Math.Clamp(original.Top + delta.Y, bounds.Top, original.Bottom);
        }
        if (handle is SelectionHandle.South or SelectionHandle.SouthWest or SelectionHandle.SouthEast)
        {
            bottom = Math.Clamp(original.Bottom + delta.Y, original.Top, bounds.Bottom);
        }
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>The area after dragging a handle by <paramref name="delta"/>, kept inside <paramref name="bounds"/>.</summary>
    public static Rect Apply(Rect original, SelectionHandle handle, Vector delta, Rect bounds)
    {
        if (handle == SelectionHandle.Inside)
        {
            double x = Math.Clamp(original.X + delta.X, bounds.Left, bounds.Right - original.Width);
            double y = Math.Clamp(original.Y + delta.Y, bounds.Top, bounds.Bottom - original.Height);
            return new Rect(x, y, original.Width, original.Height);
        }

        double left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
        if (handle is SelectionHandle.West or SelectionHandle.NorthWest or SelectionHandle.SouthWest)
        {
            left = Math.Clamp(original.Left + delta.X, bounds.Left, right - MinSize);
        }
        if (handle is SelectionHandle.East or SelectionHandle.NorthEast or SelectionHandle.SouthEast)
        {
            right = Math.Clamp(original.Right + delta.X, left + MinSize, bounds.Right);
        }
        if (handle is SelectionHandle.North or SelectionHandle.NorthWest or SelectionHandle.NorthEast)
        {
            top = Math.Clamp(original.Top + delta.Y, bounds.Top, bottom - MinSize);
        }
        if (handle is SelectionHandle.South or SelectionHandle.SouthWest or SelectionHandle.SouthEast)
        {
            bottom = Math.Clamp(original.Bottom + delta.Y, top + MinSize, bounds.Bottom);
        }
        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}
