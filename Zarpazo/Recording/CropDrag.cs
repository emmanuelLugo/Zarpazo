namespace Zarpazo.Recording;

/// <summary>
/// The rectangle of the crop dragged on the preview: which sides a press grabs (an edge, a corner, or inside it to move it
/// whole) and the crop as the pointer moves. Positions are shares of the recorded picture, so this knows nothing of windows.
/// </summary>
internal static class CropDrag
{
    /// <summary>What a press at (<paramref name="x"/>, <paramref name="y"/>) grabs.</summary>
    /// <param name="reachX">How close to a side counts as on it, as a share of the picture's width.</param>
    /// <param name="reachY">The same down the picture.</param>
    public static CropEdges HitTest(VideoCrop crop, double x, double y, double reachX, double reachY)
    {
        double left = crop.Left, right = 1 - crop.Right, top = crop.Top, bottom = 1 - crop.Bottom;
        bool acrossRange = y >= top - reachY && y <= bottom + reachY;
        bool downRange = x >= left - reachX && x <= right + reachX;
        CropEdges edges = CropEdges.None;

        // A narrow rectangle can have both sides in reach: the nearer one wins.
        double toLeft = Math.Abs(x - left), toRight = Math.Abs(x - right);
        if (acrossRange && Math.Min(toLeft, toRight) <= reachX)
        {
            edges |= toLeft <= toRight ? CropEdges.Left : CropEdges.Right;
        }
        double toTop = Math.Abs(y - top), toBottom = Math.Abs(y - bottom);
        if (downRange && Math.Min(toTop, toBottom) <= reachY)
        {
            edges |= toTop <= toBottom ? CropEdges.Top : CropEdges.Bottom;
        }

        if (edges == CropEdges.None && x > left && x < right && y > top && y < bottom)
        {
            return CropEdges.All;
        }
        return edges;
    }

    /// <summary>The crop after the grabbed sides moved by (<paramref name="dx"/>, <paramref name="dy"/>) since the press.</summary>
    /// <param name="start">The crop when the press began.</param>
    public static VideoCrop Drag(VideoCrop start, CropEdges edges, double dx, double dy)
    {
        if (edges == CropEdges.All)
        {
            // The whole rectangle slides, no further than the picture's edges.
            dx = Math.Clamp(dx, -start.Left, start.Right);
            dy = Math.Clamp(dy, -start.Top, start.Bottom);
            return new VideoCrop(start.Left + dx, start.Top + dy, start.Right - dx, start.Bottom - dy);
        }

        VideoCrop crop = start;
        if (edges.HasFlag(CropEdges.Left))
        {
            crop = crop.With(CropEdges.Left, start.Left + dx);
        }
        if (edges.HasFlag(CropEdges.Right))
        {
            crop = crop.With(CropEdges.Right, start.Right - dx);
        }
        if (edges.HasFlag(CropEdges.Top))
        {
            crop = crop.With(CropEdges.Top, start.Top + dy);
        }
        if (edges.HasFlag(CropEdges.Bottom))
        {
            crop = crop.With(CropEdges.Bottom, start.Bottom - dy);
        }
        return crop;
    }
}
