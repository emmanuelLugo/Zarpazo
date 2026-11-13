namespace Zarpazo.Recording;

/// <summary>Times as the editor shows them: 0:03.4, or 1:02:03.4 for a very long video.</summary>
internal static class TimeText
{
    public static string Format(double seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss\.f") : time.ToString(@"m\:ss\.f");
    }

    /// <summary>A mark on the ruler: the tenths only when the marks are closer than a second.</summary>
    public static string Mark(double seconds, double step)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds, 3)));
        if (step < 1)
        {
            return Format(seconds);
        }
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    /// <summary>A speed as the editor shows it: 2×, 0,5×, 1,5×.</summary>
    public static string Speed(double speed) =>
        speed.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) + "×";
}
