using System.Drawing;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>The few window tweaks the recording windows need that WPF has no property for.</summary>
internal static class WindowNative
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;  // clicks pass through to whatever is underneath
    private const int WsExToolWindow = 0x80;   // out of Alt+Tab
    private const int WsExNoActivate = 0x08000000;
    private const uint ExcludeFromCapture = 0x11;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Clicks go through it and it never takes the focus: it is only there to be looked at.</summary>
    public static void MakeClickThrough(IntPtr hwnd) =>
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);

    /// <summary>Keeps the window out of screenshots and recordings (Windows 10 2004 and later); false if Windows refuses.</summary>
    public static bool ExcludeFromScreenCapture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, ExcludeFromCapture);

    /// <summary>The scaling of the screen the area is mostly on: 1 at 100 %, 1.5 at 150 %. The pointer grows with it.</summary>
    public static double ScreenScale(Rectangle area)
    {
        var rect = new NativeRect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        IntPtr monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        try
        {
            return GetDpiForMonitor(monitor, EffectiveDpi, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1;
        }
    }

    /// <summary>Places a window in physical pixels, which WPF's own Left/Top can't do across monitors with different scaling.</summary>
    public static void Place(IntPtr hwnd, Rectangle bounds) =>
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
