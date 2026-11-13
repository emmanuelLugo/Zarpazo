using System.Windows;

namespace Zarpazo.Recording;

/// <summary>Where things sit on the timeline: the sizes, what is under the pointer, where a dragged piece would land. Pure maths.</summary>
internal static class TimelineLayout
{
    /// <summary>Space before the first piece and after the last, so their edges can be grabbed.</summary>
    public const double Padding = 14;

    public const double RulerHeight = 26;
    public const double TrackTop = RulerHeight + 6;
    public const double GripHeight = 20;
    public const double TrackHeight = 86;
    public const double Height = TrackTop + TrackHeight + 10;

    /// <summary>How close to a piece's side the pointer has to be to grab the edge.</summary>
    public const double EdgeWidth = 8;

    private static readonly double[] RulerSteps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };

    public static double Width(EditTimeline timeline, double pixelsPerSecond) => Padding * 2 + timeline.Duration * pixelsPerSecond;

    public static double XOf(double time, double pixelsPerSecond) => Padding + time * pixelsPerSecond;

    public static double TimeAt(double x, double pixelsPerSecond) => (x - Padding) / pixelsPerSecond;

    /// <summary>The left and right sides of a piece.</summary>
    public static (double Left, double Right) Span(EditTimeline timeline, int index, double pixelsPerSecond)
    {
        double start = timeline.StartOf(index);
        return (XOf(start, pixelsPerSecond), XOf(start + timeline.Segments[index].PlayLength, pixelsPerSecond));
    }

    public static TimelineHit HitTest(EditTimeline timeline, double pixelsPerSecond, Point point)
    {
        if (point.Y < 0 || point.Y > Height)
        {
            return new TimelineHit(TimelinePart.None, -1);
        }
        if (point.Y < TrackTop)
        {
            return new TimelineHit(TimelinePart.Ruler, -1);
        }
        if (point.Y > TrackTop + TrackHeight)
        {
            return new TimelineHit(TimelinePart.None, -1);
        }

        int last = timeline.Segments.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            (double left, double right) = Span(timeline, i, pixelsPerSecond);
            // The last piece takes a little of the space after it, so its end can be grabbed from outside.
            bool inside = point.X >= left && (point.X < right || i == last && point.X <= right + EdgeWidth / 2);
            if (!inside)
            {
                continue;
            }

            double edge = Math.Min(EdgeWidth, (right - left) / 3); // a very short piece keeps room in the middle
            if (point.X - left < edge)
            {
                return new TimelineHit(TimelinePart.StartEdge, i);
            }
            if (right - point.X <= edge)
            {
                return new TimelineHit(TimelinePart.EndEdge, i);
            }
            return new TimelineHit(point.Y < TrackTop + GripHeight ? TimelinePart.Grip : TimelinePart.Body, i);
        }
        return new TimelineHit(TimelinePart.None, -1);
    }

    /// <summary>
    /// The place a piece dragged by its title would take in the order, from where the middle of the dragged piece is: after
    /// every other piece whose middle is further left.
    /// </summary>
    public static int DropIndex(EditTimeline timeline, int dragged, double pixelsPerSecond, double centerX)
    {
        int place = 0;
        for (int i = 0; i < timeline.Segments.Count; i++)
        {
            if (i == dragged)
            {
                continue;
            }

            (double left, double right) = Span(timeline, i, pixelsPerSecond);
            if ((left + right) / 2 < centerX)
            {
                place++;
            }
        }
        return place;
    }

    /// <summary>The x of the line that shows where a dragged piece would land: the gap between the other pieces.</summary>
    public static double DropLineX(EditTimeline timeline, int dragged, double pixelsPerSecond, int dropIndex)
    {
        var others = Enumerable.Range(0, timeline.Segments.Count).Where(i => i != dragged).ToList();
        if (others.Count == 0 || dropIndex <= 0)
        {
            return others.Count == 0 ? Padding : Span(timeline, others[0], pixelsPerSecond).Left;
        }
        return Span(timeline, others[Math.Min(dropIndex, others.Count) - 1], pixelsPerSecond).Right;
    }

    /// <summary>The distance between the numbered marks of the ruler: the smallest round one that leaves them room for their label.</summary>
    public static double RulerStep(double pixelsPerSecond)
    {
        foreach (double step in RulerSteps)
        {
            if (step * pixelsPerSecond >= 80)
            {
                return step;
            }
        }
        return RulerSteps[^1];
    }
}
