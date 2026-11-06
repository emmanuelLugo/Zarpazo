using System.Windows;

namespace Zarpazo.Overlay;

internal static class SelectionGeometry
{
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
