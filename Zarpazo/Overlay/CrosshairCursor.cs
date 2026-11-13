using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace Zarpazo.Overlay;

/// <summary>
/// A white cross with a black outline. Windows' own cross is drawn by inverting what is under it, so over the
/// dimmed white of a light screen (mid gray) it turns into the same gray and vanishes; this one shows on anything.
/// </summary>
internal static class CrosshairCursor
{
    private static readonly Dictionary<int, Cursor> Cache = new();

    /// <summary>The cross for a screen at this scaling (1 at 100 %, 1.5 at 150 %), built once per scaling.</summary>
    public static Cursor For(double scale)
    {
        int key = (int)Math.Round(scale * 100);
        if (!Cache.TryGetValue(key, out Cursor? cursor))
        {
            cursor = Build(key / 100.0) ?? Cursors.Cross;
            Cache[key] = cursor;
        }
        return cursor;
    }

    private static Cursor? Build(double scale)
    {
        int half = (int)Math.Round(11 * scale);
        int size = 2 * half + 1;              // odd, so the hot spot is a real center pixel
        int line = (int)Math.Round(scale) | 1; // odd too, so the line sits centered on it
        int outline = Math.Max(1, (int)Math.Round(scale));

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            int start = half - line / 2;
            int thick = line + 2 * outline;
            g.FillRectangle(Brushes.Black, start - outline, 0, thick, size);
            g.FillRectangle(Brushes.Black, 0, start - outline, size, thick);
            g.FillRectangle(Brushes.White, start, outline, line, size - 2 * outline);
            g.FillRectangle(Brushes.White, outline, start, size - 2 * outline, line);
        }

        IntPtr icon = bitmap.GetHicon();
        try
        {
            if (!GetIconInfo(icon, out IconInfo info))
                return null;
            try
            {
                info.IsIcon = false; // a cursor, with its hot spot in the middle of the cross
                info.HotspotX = info.HotspotY = half;
                IntPtr handle = CreateIconIndirect(ref info);
                return handle == IntPtr.Zero ? null : CursorInteropHelper.Create(new CursorHandle(handle));
            }
            finally
            {
                DeleteObject(info.Mask);
                DeleteObject(info.Color);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private sealed class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CursorHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => DestroyCursor(handle);
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
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
