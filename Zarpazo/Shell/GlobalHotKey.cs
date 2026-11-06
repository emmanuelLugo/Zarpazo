using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Zarpazo.Shell;

/// <summary>One system-wide shortcut, which can be moved to another key combination while the app runs.</summary>
internal sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 1;
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Action _onPressed;
    private readonly HwndSource _window;

    public bool IsRegistered { get; private set; }

    public GlobalHotKey(Action onPressed)
    {
        _onPressed = onPressed;
        _window = new HwndSource(new HwndSourceParameters("ZarpazoHotKey")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
    }

    /// <summary>Replaces the current shortcut. Returns false, with nothing registered, if another program already owns the combination.</summary>
    public bool TryRegister(HotKeyBinding binding)
    {
        Unregister();
        IsRegistered = RegisterHotKey(_window.Handle, HotKeyId, binding.Modifiers | ModNoRepeat, binding.VirtualKey);
        return IsRegistered;
    }

    public void Unregister()
    {
        if (IsRegistered)
        {
            UnregisterHotKey(_window.Handle, HotKeyId);
            IsRegistered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
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
