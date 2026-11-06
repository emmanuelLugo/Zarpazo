using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Zarpazo.Shell;

internal sealed class PrintScreenHotKey : IDisposable
{
    private const int HotKeyId = 1;
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSnapshot = 0x2C;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Action _onPressed;
    private readonly HwndSource _window;

    public bool IsRegistered { get; }

    public PrintScreenHotKey(Action onPressed)
    {
        _onPressed = onPressed;
        _window = new HwndSource(new HwndSourceParameters("ZarpazoHotKey")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
        IsRegistered = RegisterHotKey(_window.Handle, HotKeyId, ModNoRepeat, VkSnapshot);
    }

    public void Dispose()
    {
        if (IsRegistered)
        {
            UnregisterHotKey(_window.Handle, HotKeyId);
        }
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam == HotKeyId)
        {
            handled = true;
            _onPressed();
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
