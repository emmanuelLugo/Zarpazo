using System.Drawing;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>The real keyboard and mouse, read with GetAsyncKeyState (which works from any program, no hook needed).</summary>
internal sealed class WindowsInputState : IInputState
{
    private const int DownBit = 0x8000;
    private const int PressedSinceBit = 0x0001;

    public bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & DownBit) != 0;

    public bool WasPressedSinceLastCheck(int virtualKey) => (GetAsyncKeyState(virtualKey) & PressedSinceBit) != 0;

    public Point CursorPosition => GetCursorPos(out NativePoint point) ? new Point(point.X, point.Y) : Point.Empty;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
