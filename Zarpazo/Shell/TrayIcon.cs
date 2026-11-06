using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Zarpazo.Shell;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onCapture, Action onExit)
    {
        var startup = new StartupRegistration();
        var startupItem = new ToolStripMenuItem("Iniciar con Windows") { CheckOnClick = true };
        startupItem.Click += (_, _) => ApplyStartupChoice(startup, startupItem);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar", null, (_, _) => onCapture());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => onExit());
        // Read it every time: the entry can change outside the app, or point at another copy of the program.
        menu.Opening += (_, _) => startupItem.Checked = startup.IsEnabled;

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

    private void ApplyStartupChoice(StartupRegistration startup, ToolStripMenuItem item)
    {
        try
        {
            startup.SetEnabled(item.Checked);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // A policy can block writing to the Run key. Undo the tick so the menu doesn't claim something false.
            item.Checked = !item.Checked;
            ShowWarning("No se pudo cambiar el inicio con Windows. Es posible que una directiva del sistema lo bloquee.");
        }
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
