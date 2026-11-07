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
    public static ToolbarPlacement Place(Rect area, Size screen, Size tools, Size actions)
    {
        double actionsLeft = Math.Clamp(area.Right - actions.Width, 0, Math.Max(0, screen.Width - actions.Width));
        double actionsTop = area.Bottom + Gap;
        if (actionsTop + actions.Height > screen.Height)
        {
            actionsTop = area.Top - actions.Height - Gap;
        }
        if (actionsTop < 0)
        {
            actionsTop = Math.Max(0, area.Bottom - actions.Height - Gap);
        }

        bool onRight = true;
        double toolsLeft = area.Right + Gap;
        if (toolsLeft + tools.Width > screen.Width)
        {
            toolsLeft = area.Left - tools.Width - Gap;
            onRight = false;
        }
        if (toolsLeft < 0)
        {
            toolsLeft = Math.Max(0, area.Right - tools.Width - Gap);
            onRight = true;
        }
        double toolsTop = Math.Clamp(area.Top, 0, Math.Max(0, screen.Height - tools.Height));

        var actionsRect = new Rect(actionsLeft, actionsTop, actions.Width, actions.Height);
        var toolsRect = new Rect(toolsLeft, toolsTop, tools.Width, tools.Height);
        if (toolsRect.IntersectsWith(actionsRect))
        {
            double above = actionsRect.Top - Gap - tools.Height;
            double below = actionsRect.Bottom + Gap;
            if (above >= 0)
            {
                toolsTop = above;
            }
            else if (below + tools.Height <= screen.Height)
            {
                toolsTop = below;
            }
            else if (actionsRect.Left - Gap - tools.Width >= 0)
            {
                toolsLeft = actionsRect.Left - Gap - tools.Width; // cramped: side by side
                onRight = false;
            }
        }

        return new ToolbarPlacement(new Point(toolsLeft, toolsTop), new Point(actionsLeft, actionsTop), onRight);
    }
}
