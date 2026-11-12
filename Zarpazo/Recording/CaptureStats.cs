using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Zarpazo.Recording;

/// <summary>
/// How the last recording's pictures were taken: how long each step took, how many came late, and whether the graphics card
/// copied the screen. Written to a small text file in Zarpazo's temporary folder after every video, to find out why a
/// recording plays unevenly on a particular PC.
/// </summary>
internal sealed class CaptureStats
{
    private readonly List<double> _grabMs = [];
    private readonly List<GrabTimings> _steps = [];
    private readonly List<double> _loopMs = [];
    private long _lastPicture = -1;

    public int Late { get; private set; }

    public int Dropped { get; private set; }

    /// <summary>One picture: how long copying the screen took and whether it was taken more than a few ms after its turn.</summary>
    public void Picture(double grabMs, bool late, bool dropped, GrabTimings steps)
    {
        _steps.Add(steps);
        long now = Stopwatch.GetTimestamp();
        if (_lastPicture >= 0)
        {
            _loopMs.Add(Stopwatch.GetElapsedTime(_lastPicture, now).TotalMilliseconds);
        }
        _lastPicture = now;
        _grabMs.Add(grabMs);
        Late += late ? 1 : 0;
        Dropped += dropped ? 1 : 0;
    }

    /// <summary>Writes the summary over the previous one; a failure to write is ignored, it is only for diagnosis.</summary>
    public void Save(string folder, string video, int fps, string method, string? duplicatorReport)
    {
        try
        {
            var text = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            text.AppendLine(c, $"Video: {Path.GetFileName(video)}  ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            text.AppendLine(c, $"Copia de pantalla: {method}");
            if (duplicatorReport != null)
            {
                text.AppendLine(c, $"Tarjeta de video: {duplicatorReport}");
            }
            text.AppendLine(c, $"Cuadros: {_grabMs.Count} a {fps} por segundo; tarde: {Late}; descartados por el codificador: {Dropped}");
            text.AppendLine(c, $"Copiar la pantalla (ms): {Summary(_grabMs)}");
            text.AppendLine(c, $"Entre cuadros (ms): {Summary(_loopMs)}");
            text.AppendLine(c, $"  leer teclado y mouse: {Summary(_steps.Select(s => s.InputMs).ToList())}");
            text.AppendLine(c, $"  copiar la pantalla: {Summary(_steps.Select(s => s.CopyMs).ToList())} (por la tarjeta: {_steps.Count(s => s.ByGraphicsCard)} de {_steps.Count})");
            text.AppendLine(c, $"  dibujar puntero y efectos: {Summary(_steps.Select(s => s.DrawMs).ToList())}");
            text.AppendLine(c, $"  achicar: {Summary(_steps.Select(s => s.ScaleMs).ToList())}");
            text.AppendLine(c, $"  pasar al video: {Summary(_steps.Select(s => s.OutMs).ToList())}");
            File.WriteAllText(Path.Combine(folder, "ultima-grabacion.txt"), text.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Summary(List<double> values)
    {
        if (values.Count == 0)
        {
            return "-";
        }
        List<double> sorted = values.Order().ToList();
        double At(double share) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * share))];
        return string.Create(CultureInfo.InvariantCulture,
            $"media {values.Average():0.0}, mediana {At(0.5):0.0}, 90% {At(0.9):0.0}, máx {sorted[^1]:0.0}");
    }
}
