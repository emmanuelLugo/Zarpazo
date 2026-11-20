using System.Drawing;
using System.Text;
using Zarpazo.Recording;
using Zarpazo.Update;

namespace Zarpazo.Diagnostics;

/// <summary>The text a person pastes into a bug report: versions, screens and the last log lines. No paths of captures, no names.</summary>
internal static class ErrorReport
{
    public const int LogLines = 60;

    public static string Build(ErrorLog log, IEnumerable<(Rectangle Bounds, double Scale)> screens)
    {
        var text = new StringBuilder();
        text.AppendLine($"Zarpazo {AppVersion.Current}");
        text.AppendLine($"Windows {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64" : "32")} bits)");
        foreach (var (bounds, scale) in screens)
        {
            text.AppendLine($"Pantalla {bounds.Width}x{bounds.Height} en ({bounds.X}, {bounds.Y}), escala {scale:P0}");
        }
        string tail = log.Tail(LogLines);
        text.AppendLine().AppendLine(tail.Length == 0 ? "Sin errores registrados." : "Últimas líneas del registro:").AppendLine(tail);
        return text.ToString();
    }

    public static string Build(ErrorLog log) => Build(log,
        System.Windows.Forms.Screen.AllScreens.Select(screen => (screen.Bounds, WindowNative.ScreenScale(screen.Bounds))));
}
