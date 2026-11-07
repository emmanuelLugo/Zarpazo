using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Zarpazo.Theme;

/// <summary>Makes a window's title bar light or dark with the rest of the colours; WPF doesn't paint it, Windows does.</summary>
internal static class TitleBarTheme
{
    private const int UseImmersiveDarkMode = 20; // 19 on Windows 10 before version 2004

    /// <summary>Sets the title bar now and every time the theme changes, until the window closes.</summary>
    public static void Follow(Window window)
    {
        void Update()
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                SetDark(handle, ThemeManager.IsDark);
            }
        }

        window.SourceInitialized += (_, _) => Update(); // before the window is shown, so it never flashes the wrong colour
        ThemeManager.Changed += Update;
        window.Closed += (_, _) => ThemeManager.Changed -= Update;
    }

    private static void SetDark(IntPtr window, bool dark)
    {
        int value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(window, 19, ref value, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
