using System.Drawing;
using System.Runtime.InteropServices;

namespace Zarpazo.Guide;

/// <summary>
/// Tells when the left mouse button goes down anywhere on the screen. A low-level hook is the only way to hear it before the
/// click reaches the program under the pointer, which is what lets the picture show the screen as it was when the person clicked.
/// The callback runs on the thread that made the hook (the UI one) and must be quick.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private readonly HookProc _proc; // kept in a field: if it were collected Windows would call into nothing
    private IntPtr _hook;

    public MouseHook()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows no permitió escuchar los clics.");
        }
    }

    /// <summary>The left button went down here, in physical screen pixels.</summary>
    public event Action<Point>? LeftButtonDown;

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == WmLButtonDown)
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseData>(lParam);
                LeftButtonDown?.Invoke(new Point(data.X, data.Y));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Never let a failure here reach Windows: the click must go on to the program it was meant for.
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
