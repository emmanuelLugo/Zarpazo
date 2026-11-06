using System.Drawing;
using System.Windows.Forms;

namespace Zarpazo.Shell;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onCapture, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar", null, (_, _) => onCapture());
        menu.Items.Add("Salir", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            // shortcut: generic system icon, replace it when the app has its own .ico.
            Icon = SystemIcons.Application,
            Text = "Zarpazo",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    public void ShowWarning(string message) =>
        _icon.ShowBalloonTip(5000, "Zarpazo", message, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
