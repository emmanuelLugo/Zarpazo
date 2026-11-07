using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Zarpazo.Shell;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onCapture, Action onHistory, Action onSettings, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar", null, (_, _) => onCapture());
        menu.Items.Add("Historial…", null, (_, _) => onHistory());
        menu.Items.Add("Configuración…", null, (_, _) => onSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
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

    // Asking for the small-icon size picks the 16, 24 or 32 px entry that matches the screen's scaling,
    // instead of shrinking the 32 px one.
    private static Icon LoadIcon()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Zarpazo.ico")
            ?? throw new InvalidOperationException("The embedded Zarpazo.ico is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
