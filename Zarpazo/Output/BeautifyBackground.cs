using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Output;

/// <summary>A diagonal gradient the capture is placed on.</summary>
internal sealed record BeautifyBackground(string Key, string Name, Color From, Color To)
{
    public static IReadOnlyList<BeautifyBackground> All { get; } =
    [
        new("violeta", "Violeta", Hex("#667EEA"), Hex("#764BA2")),
        new("oceano", "Océano", Hex("#2193B0"), Hex("#6DD5ED")),
        new("atardecer", "Atardecer", Hex("#F6D365"), Hex("#FDA085")),
        new("bosque", "Bosque", Hex("#11998E"), Hex("#38EF7D")),
        new("lavanda", "Lavanda", Hex("#A18CD1"), Hex("#FBC2EB")),
        new("noche", "Noche", Hex("#232526"), Hex("#414345")),
        new("gris", "Gris claro", Hex("#D7DDE8"), Hex("#F5F7FA")),
        new("blanco", "Blanco", Hex("#FFFFFF"), Hex("#FFFFFF")),
        new("aurora", "Aurora", Hex("#00C9FF"), Hex("#92FE9D")),
        new("fuego", "Fuego", Hex("#F12711"), Hex("#F5AF19")),
        new("rosa", "Rosa", Hex("#FF9A9E"), Hex("#FECFEF")),
        new("medianoche", "Medianoche", Hex("#0F2027"), Hex("#2C5364")),
        new("durazno", "Durazno", Hex("#FFECD2"), Hex("#FCB69F")),
        new("cielo", "Cielo", Hex("#A1C4FD"), Hex("#C2E9FB")),
        new("uva", "Uva", Hex("#8E2DE2"), Hex("#4A00E0")),
        new("menta", "Menta", Hex("#43E97B"), Hex("#38F9D7")),
        new("coral", "Coral", Hex("#FF7E5F"), Hex("#FEB47B")),
        new("grafito", "Grafito", Hex("#141E30"), Hex("#243B55")),
    ];

    public static BeautifyBackground Default => All[0];

    /// <summary>The background with that key; anything unknown (a hand-edited file) gives the default.</summary>
    public static BeautifyBackground Find(string? key) => All.FirstOrDefault(background => background.Key == key) ?? Default;

    public Brush ToBrush()
    {
        var brush = new LinearGradientBrush(From, To, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);
}
