using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.Recording;

/// <summary>
/// The background behind the recording, kept as one text so the frame's style stays a few plain values:
/// the key of one of <see cref="BeautifyBackground.All"/> ("violeta"), a colour or a gradient of the user's own
/// ("custom:#RRGGBB:#RRGGBB:45", the same colour twice for a plain one) or a picture from the disk ("image:C:\fondos\mar.jpg").
/// </summary>
internal static class FrameBackground
{
    private const string CustomPrefix = "custom:";
    private const string ImagePrefix = "image:";

    public const int DefaultAngle = 45; // the corner-to-corner slant of the built-in gradients

    /// <summary>The text for a colour or gradient of one's own. The angle goes clockwise from left to right: 45 is top-left to bottom-right.</summary>
    public static string Custom(Color from, Color to, int angle) =>
        $"{CustomPrefix}{Hex(from)}:{Hex(to)}:{NormalAngle(angle).ToString(CultureInfo.InvariantCulture)}";

    public static string Image(string path) => ImagePrefix + path;

    public static bool IsImage(string? key) => key != null && key.StartsWith(ImagePrefix, StringComparison.Ordinal) && key.Length > ImagePrefix.Length;

    public static string? ImagePath(string? key) => IsImage(key) ? key![ImagePrefix.Length..] : null;

    /// <summary>The colours and slant of a built-in or custom background; false for a picture.</summary>
    public static bool TryColors(string? key, out Color from, out Color to, out int angle)
    {
        if (TryParseCustom(key, out from, out to, out angle))
        {
            return true;
        }
        if (!IsImage(key))
        {
            BeautifyBackground built = BeautifyBackground.Find(key);
            (from, to, angle) = (built.From, built.To, DefaultAngle);
            return true;
        }
        (from, to, angle) = (default, default, DefaultAngle);
        return false;
    }

    /// <summary>The key as it is stored: a built-in one that exists, a well-formed custom one in its normal spelling, a picture's path, or else the default.</summary>
    public static string Tidy(string? key)
    {
        if (TryParseCustom(key, out Color from, out Color to, out int angle))
        {
            return Custom(from, to, angle);
        }
        if (IsImage(key))
        {
            return key!; // a missing file is drawn as the default, and comes back if the file does
        }
        return BeautifyBackground.Find(key).Key;
    }

    /// <summary>The brush that paints the background. A picture that cannot be read is painted as the default background.</summary>
    public static Brush ToBrush(string? key)
    {
        if (ImagePath(key) is { } path)
        {
            if (TryLoadImage(path) is { } picture)
            {
                var image = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
                image.Freeze();
                return image;
            }
            return BeautifyBackground.Default.ToBrush();
        }

        if (TryParseCustom(key, out Color from, out Color to, out int angle))
        {
            double radians = angle * Math.PI / 180;
            // The line runs from edge to edge of the picture whatever the slant (45 reaches the corners, as the built-in ones do).
            double cos = Math.Cos(radians), sin = Math.Sin(radians);
            double reach = 0.5 / Math.Max(Math.Abs(cos), Math.Abs(sin));
            double dx = cos * reach, dy = sin * reach;
            var brush = new LinearGradientBrush(from, to, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
            brush.Freeze();
            return brush;
        }
        return BeautifyBackground.Find(key).ToBrush();
    }

    /// <summary>A pleasant gradient picked at random: two colours of neighbouring hues, light and bright, with a slant.</summary>
    public static string RandomGradient(Random random)
    {
        double hue = random.NextDouble() * 360;
        double shift = 30 + random.NextDouble() * 50;
        Color from = FromHsv(hue, 0.55 + random.NextDouble() * 0.3, 0.75 + random.NextDouble() * 0.25);
        Color to = FromHsv((hue + shift) % 360, 0.55 + random.NextDouble() * 0.3, 0.55 + random.NextDouble() * 0.3);
        return Custom(from, to, random.Next(0, 24) * 15);
    }

    private static BitmapImage? TryLoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var picture = new BitmapImage();
            picture.BeginInit();
            picture.CacheOption = BitmapCacheOption.OnLoad; // read now, so the file is not held open
            picture.DecodePixelWidth = 2560; // a backdrop never needs more, and a photo can be huge
            picture.UriSource = new Uri(path, UriKind.Absolute);
            picture.EndInit();
            picture.Freeze();
            return picture;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or UriFormatException or InvalidOperationException)
        {
            return null; // not a picture Windows can read
        }
    }

    private static bool TryParseCustom(string? key, out Color from, out Color to, out int angle)
    {
        from = to = default;
        angle = DefaultAngle;
        if (key == null || !key.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        string[] parts = key[CustomPrefix.Length..].Split(':');
        if (parts.Length != 3 || !TryHex(parts[0], out from) || !TryHex(parts[1], out to)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out angle))
        {
            return false;
        }
        angle = NormalAngle(angle);
        return true;
    }

    private static int NormalAngle(int angle) => ((angle % 360) + 360) % 360;

    /// <summary>A colour as "#RRGGBB".</summary>
    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Reads "#RRGGBB" (any case).</summary>
    public static bool TryHex(string? text, out Color color)
    {
        color = default;
        if (text == null || text.Length != 7 || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return false;
        }
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - chroma;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
