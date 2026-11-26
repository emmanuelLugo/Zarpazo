using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>
/// What the rows under the timeline share: a flat strip behind their blocks and, while a row is empty, an icon with a
/// short hint in the middle of the part of the row that can be seen.
/// </summary>
internal static class LaneBackdrop
{
    private const double IconSize = 14;
    private const double Gap = 8;

    public static void Draw(DrawingContext dc, FrameworkElement lane, Rect strip)
    {
        dc.PushOpacity(0.6);
        dc.DrawRoundedRectangle(lane.TryFindResource("Hover") as Brush ?? Brushes.Gray, null, strip, 8, 8);
        dc.Pop();
    }

    /// <param name="icon">Drawn as lines, on a 24 × 24 grid.</param>
    public static void DrawHint(DrawingContext dc, FrameworkElement lane, Rect strip, Geometry icon, string text)
    {
        Brush brush = lane.TryFindResource("TextSecondary") as Brush ?? Brushes.Silver;
        var words = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, brush,
            VisualTreeHelper.GetDpi(lane).PixelsPerDip);

        double width = IconSize + Gap + words.Width;
        double left = Math.Clamp(VisibleCentre(lane, strip) - width / 2, strip.Left + Gap, Math.Max(strip.Left + Gap, strip.Right - width - Gap));
        double scale = IconSize / 24;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, strip.Top + (strip.Height - IconSize) / 2));
        dc.DrawGeometry(null, new Pen(brush, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, icon);
        dc.Pop();
        dc.DrawText(words, new Point(left + IconSize + Gap, strip.Top + (strip.Height - words.Height) / 2));
    }

    // With the timeline zoomed in the row is far wider than the window: the hint stays where it can be read.
    private static double VisibleCentre(FrameworkElement lane, Rect strip)
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(lane); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer { ViewportWidth: > 0 } scroller)
            {
                return scroller.HorizontalOffset + scroller.ViewportWidth / 2;
            }
        }
        return strip.Left + strip.Width / 2;
    }
}
