using System.Drawing;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>
/// CopyFromScreen leaves the mouse pointer out, and in a demonstration it is the part people follow. It can be drawn bigger
/// than on screen: Windows' own pointers (arrow, hand, text…) are loaded again at the bigger size from their files, which
/// hold large versions, so they stay sharp; any other pointer is stretched.
/// </summary>
internal static class CursorPainter
{
    private const int CursorShowing = 0x1;
    private const int DrawNormal = 0x3;
    private const int ImageCursor = 2;
    private const int CursorWidthMetric = 13; // SM_CXCURSOR

    // The pointers Windows comes with, by the number LoadCursor and LoadImage both know them by.
    private static readonly int[] SystemCursors =
    [
        32512, // arrow
        32513, // text
        32514, // wait
        32515, // cross
        32516, // up arrow
        32642, 32643, 32644, 32645, 32646, // the four resize arrows and the move cross
        32648, // not allowed
        32649, // hand
        32650, // working in the background
        32651, // help
    ];

    private static readonly Dictionary<(int Id, int Size), (IntPtr Cursor, int HotspotX, int HotspotY)> Large = [];
    private static Dictionary<IntPtr, int>? _systemHandles;

    /// <param name="origin">Where the picture being drawn on starts, in screen pixels.</param>
    /// <param name="scale">How big: 1 as on screen.</param>
    public static void Draw(Graphics graphics, Point origin, double scale = 1)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || (info.Flags & CursorShowing) == 0 || !GetIconInfo(info.Cursor, out IconInfo icon))
        {
            return;
        }

        try
        {
            IntPtr cursor = info.Cursor;
            int hotspotX = icon.HotspotX, hotspotY = icon.HotspotY, width = 0, height = 0;
            if (scale > 1.01)
            {
                (int Width, int Height) size = Size(icon);
                int wanted = (int)Math.Round(Math.Max(size.Width, GetSystemMetrics(CursorWidthMetric)) * scale);
                if (LargeSystemCursor(info.Cursor, wanted) is { } large)
                {
                    (cursor, hotspotX, hotspotY) = large; // drawn at the size it was loaded at
                }
                else
                {
                    width = (int)Math.Round(size.Width * scale);
                    height = (int)Math.Round(size.Height * scale);
                    hotspotX = (int)Math.Round(icon.HotspotX * scale);
                    hotspotY = (int)Math.Round(icon.HotspotY * scale);
                }
            }

            IntPtr hdc = graphics.GetHdc();
            try
            {
                DrawIconEx(hdc, info.X - hotspotX - origin.X, info.Y - hotspotY - origin.Y, cursor, width, height, 0, IntPtr.Zero, DrawNormal);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        finally
        {
            // GetIconInfo hands over copies of the bitmaps, which are ours to free.
            if (icon.Mask != IntPtr.Zero)
            {
                DeleteObject(icon.Mask);
            }
            if (icon.Color != IntPtr.Zero)
            {
                DeleteObject(icon.Color);
            }
        }
    }

    // The same Windows pointer loaded at a bigger size, or null for a pointer of some program.
    private static (IntPtr Cursor, int HotspotX, int HotspotY)? LargeSystemCursor(IntPtr current, int size)
    {
        _systemHandles ??= SystemCursors.ToDictionary(id => LoadCursor(IntPtr.Zero, id), id => id);
        if (!_systemHandles.TryGetValue(current, out int id))
        {
            return null;
        }

        if (!Large.TryGetValue((id, size), out var large))
        {
            IntPtr loaded = LoadImage(IntPtr.Zero, id, ImageCursor, size, size, 0);
            if (loaded == IntPtr.Zero || !GetIconInfo(loaded, out IconInfo icon))
            {
                return null;
            }
            DeleteObject(icon.Mask);
            if (icon.Color != IntPtr.Zero)
            {
                DeleteObject(icon.Color);
            }
            large = (loaded, icon.HotspotX, icon.HotspotY); // kept for the life of the app: a handful of small pictures
            Large[(id, size)] = large;
        }
        return large;
    }

    // A pointer's size in pixels; a black-and-white one keeps two pictures one over the other in its mask.
    private static (int Width, int Height) Size(IconInfo icon)
    {
        if (GetObject(icon.Mask, Marshal.SizeOf<BitmapInfo>(), out BitmapInfo mask) == 0)
        {
            int metric = GetSystemMetrics(CursorWidthMetric);
            return (metric, metric);
        }
        return (mask.Width, icon.Color == IntPtr.Zero ? mask.Height / 2 : mask.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, int id);

    [DllImport("user32.dll", EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(IntPtr instance, int id, int type, int width, int height, int flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BitmapInfo info);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
