using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Zarpazo.Capture;

internal static class WindowFinder
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>Bounds, in physical pixels, of the windows a user could want to capture, from the topmost down.</summary>
    public static List<Rectangle> GetVisibleWindowBounds()
    {
        var result = new List<Rectangle>();
        EnumWindows((hwnd, _) =>
        {
            if (TryGetBounds(hwnd, out Rectangle bounds))
            {
                result.Add(bounds);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool TryGetBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = default;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        // Windows on another virtual desktop or hidden UWP shells report "visible" but are cloaked.
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        string cls = className.ToString();
        if (cls is "Progman" or "WorkerW")
        {
            return false; // the desktop itself
        }

        // shortcut: untitled windows are skipped because most are invisible helpers; a real untitled window won't be
        // highlightable. Revisit if someone reports one that should be.
        if (GetWindowTextLength(hwnd) == 0 && cls != "Shell_TrayWnd")
        {
            return false;
        }

        // The frame bounds exclude the invisible resize border that GetWindowRect includes on Windows 10 and 11.
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect rect, Marshal.SizeOf<Rect>()) != 0
            && !GetWindowRect(hwnd, out rect))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
