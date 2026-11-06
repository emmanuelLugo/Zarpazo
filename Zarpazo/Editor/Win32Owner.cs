using System.Windows.Forms;

namespace Zarpazo.Editor;

/// <summary>Lets a WinForms dialog be owned by a WPF window, so it opens in front of the always-on-top overlay.</summary>
internal sealed class Win32Owner(IntPtr handle) : IWin32Window
{
    public IntPtr Handle { get; } = handle;
}
