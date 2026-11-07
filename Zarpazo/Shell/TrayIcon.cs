using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zarpazo.Shell;

internal sealed class TrayIcon : IDisposable
{
    private static readonly Color RecordingColor = Color.FromArgb(0xE5, 0x39, 0x35);
    private static readonly Color PausedColor = Color.FromArgb(0xFD, 0xD8, 0x35);

    /// <summary>Seconds to wait before a delayed capture; long enough to open a menu or hover over something.</summary>
    internal static readonly int[] DelayChoices = [3, 5, 10];

    private readonly NotifyIcon _icon;
    private readonly Icon _normalIcon;
    private readonly ToolStripItem _stopRecording;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _delayedMenu;
    private Icon? _recordingIcon;
    private Icon? _pausedIcon;

    public TrayIcon(Action onCapture, Action onFullScreen, Action onRepeatLast, Func<bool> canRepeatLast, Action<int> onDelayed,
        Action onHistory, Action onSettings, Action onStopRecording, Action onExit)
    {
        var menu = new ContextMenuStrip();
        _menu = menu;
        menu.Items.Add("Capturar", null, (_, _) => onCapture());
        menu.Items.Add("Pantalla completa", null, (_, _) => onFullScreen());
        ToolStripItem repeat = menu.Items.Add("Repetir la última zona", null, (_, _) => onRepeatLast());
        menu.Opening += (_, _) => repeat.Enabled = canRepeatLast(); // there is nothing to repeat until one has been captured
        var delayed = new ToolStripMenuItem("Capturar con retardo");
        foreach (int seconds in DelayChoices)
        {
            delayed.DropDownItems.Add($"{seconds} segundos", null, (_, _) => onDelayed(seconds));
        }
        menu.Items.Add(delayed);
        _delayedMenu = delayed;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Historial…", null, (_, _) => onHistory());
        menu.Items.Add("Configuración…", null, (_, _) => onSettings());
        _stopRecording = menu.Items.Add("Detener grabación", null, (_, _) => onStopRecording());
        _stopRecording.Visible = false; // only while recording
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => onExit());

        _normalIcon = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _normalIcon,
            Text = "Zarpazo",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    /// <summary>Paints the menu dark or back to the colours Windows gives it.</summary>
    public void SetDark(bool dark)
    {
        foreach (ToolStripDropDown dropDown in new ToolStripDropDown[] { _menu, _delayedMenu.DropDown })
        {
            if (dark)
            {
                dropDown.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false };
                dropDown.BackColor = DarkMenuColors.Background;
                dropDown.ForeColor = DarkMenuColors.Text;
            }
            else
            {
                dropDown.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                dropDown.ResetBackColor();
                dropDown.ResetForeColor();
            }
        }
    }

    public void ShowWarning(string message) =>
        _icon.ShowBalloonTip(5000, "Zarpazo", message, ToolTipIcon.Warning);

    public void ShowInfo(string message) =>
        _icon.ShowBalloonTip(3000, "Zarpazo", message, ToolTipIcon.Info);

    /// <summary>
    /// Shows that a recording is under way: a red dot on the icon, an amber one while paused, and a menu entry to stop it.
    /// That way it is visible even when the bar is out of sight.
    /// </summary>
    public void SetRecording(bool active, bool paused)
    {
        _stopRecording.Visible = active;
        if (!active)
        {
            _icon.Icon = _normalIcon;
            _icon.Text = "Zarpazo";
            return;
        }

        _icon.Icon = paused
            ? _pausedIcon ??= WithBadge(_normalIcon, PausedColor)
            : _recordingIcon ??= WithBadge(_normalIcon, RecordingColor);
        _icon.Text = paused ? "Zarpazo · grabación en pausa" : "Zarpazo · grabando";
    }

    public void Dispose()
    {
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _recordingIcon?.Dispose();
        _pausedIcon?.Dispose();
        _normalIcon.Dispose();
    }

    // Asking for the small-icon size picks the 16, 24 or 32 px entry that matches the screen's scaling,
    // instead of shrinking the 32 px one.
    private static Icon LoadIcon()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Zarpazo.ico")
            ?? throw new InvalidOperationException("The embedded Zarpazo.ico is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    // The icon with a dot in its bottom-right corner, white-rimmed so it shows on both light and dark taskbars.
    private static Icon WithBadge(Icon source, Color color)
    {
        using Bitmap bitmap = source.ToBitmap();
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int diameter = Math.Max(7, bitmap.Width / 2 + 1);
            var area = new Rectangle(bitmap.Width - diameter, bitmap.Height - diameter, diameter - 1, diameter - 1);
            using var fill = new SolidBrush(color);
            using var rim = new Pen(Color.White, Math.Max(1f, bitmap.Width / 16f));
            graphics.FillEllipse(fill, area);
            graphics.DrawEllipse(rim, area);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone(); // the clone owns its own copy, so the handle can be freed
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
