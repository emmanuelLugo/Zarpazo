using System.Windows;

namespace Zarpazo.Editor;

/// <summary>Where the two bars go around the chosen area: the drawing tools beside it, the actions under it.</summary>
/// <param name="Tools">Top-left corner of the tools column.</param>
/// <param name="Actions">Top-left corner of the actions row.</param>
/// <param name="ToolsOnRight">True when the column stands right of the area, so its pop-ups open further right.</param>
internal readonly record struct ToolbarPlacement(Point Tools, Point Actions, bool ToolsOnRight);

internal static class ToolbarLayout
{
    public const double Gap = 6;

    /// <summary>
    /// The actions go under the area, right-aligned with it; above it when there is no room below, and inside it when the area
    /// fills the screen. The tools go right of the area, level with its top; left of it when there is no room; inside it when
    /// neither side fits. If the two end up on top of each other the tools move out of the way.
    /// </summary>
    public static ToolbarPlacement Place(Rect area, Size screen, Size tools, Size actions) =>
        Place(area, new Rect(screen), tools, actions);

    /// <summary>The same, kept inside <paramref name="screen"/>: the monitor the area is on, so the bars never land on another one.</summary>
    public static ToolbarPlacement Place(Rect area, Rect screen, Size tools, Size actions)
    {
        double actionsLeft = Math.Clamp(area.Right - actions.Width, screen.Left, Math.Max(screen.Left, screen.Right - actions.Width));
        double actionsTop = area.Bottom + Gap;
        if (actionsTop + actions.Height > screen.Bottom)
        {
            actionsTop = area.Top - actions.Height - Gap;
        }
        if (actionsTop < screen.Top)
        {
            actionsTop = Math.Max(screen.Top, Math.Min(area.Bottom, screen.Bottom) - actions.Height - Gap);
        }

        bool onRight = true;
        bool inside = false;
        double toolsLeft = area.Right + Gap;
        if (toolsLeft + tools.Width > screen.Right)
        {
            toolsLeft = area.Left - tools.Width - Gap;
            onRight = false;
        }
        if (toolsLeft < screen.Left)
        {
            toolsLeft = Math.Max(screen.Left, Math.Min(area.Right, screen.Right) - tools.Width - Gap);
            onRight = true;
            inside = true;
        }
        // Inside the area the column keeps a gap from its top edge too, instead of sticking to it.
        double toolsTop = Math.Clamp(inside ? area.Top + Gap : area.Top, screen.Top, Math.Max(screen.Top, screen.Bottom - tools.Height));

        var actionsRect = new Rect(actionsLeft, actionsTop, actions.Width, actions.Height);
        var toolsRect = new Rect(toolsLeft, toolsTop, tools.Width, tools.Height);
        if (toolsRect.IntersectsWith(actionsRect))
        {
            double above = actionsRect.Top - Gap - tools.Height;
            double below = actionsRect.Bottom + Gap;
            if (above >= screen.Top)
            {
                toolsTop = above;
            }
            else if (below + tools.Height <= screen.Bottom)
            {
                toolsTop = below;
            }
            else if (actionsRect.Left - Gap - tools.Width >= screen.Left)
            {
                toolsLeft = actionsRect.Left - Gap - tools.Width; // cramped: side by side
                onRight = false;
            }
        }

        return new ToolbarPlacement(new Point(toolsLeft, toolsTop), new Point(actionsLeft, actionsTop), onRight);
    }
}
