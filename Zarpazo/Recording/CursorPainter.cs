using System.Drawing;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>CopyFromScreen leaves the mouse pointer out, and in a demonstration it is the part people follow.</summary>
internal static class CursorPainter
{
    private const int CursorShowing = 0x1;
    private const int DrawNormal = 0x3;

    /// <param name="origin">Where the picture being drawn on starts, in screen pixels.</param>
    public static void Draw(Graphics graphics, Point origin)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || (info.Flags & CursorShowing) == 0 || !GetIconInfo(info.Cursor, out IconInfo icon))
        {
            return;
        }

        try
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                DrawIconEx(hdc, info.X - icon.HotspotX - origin.X, info.Y - icon.HotspotY - origin.Y, info.Cursor, 0, 0, 0, IntPtr.Zero, DrawNormal);
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

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
