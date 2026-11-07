using System.IO;
using System.Text.RegularExpressions;

namespace Zarpazo.Output;

/// <summary>Builds file names from a template such as "Zarpazo {fecha} {hora}", and makes sure they are safe and not taken.</summary>
internal static partial class FileNameTemplate
{
    public const string Default = "Zarpazo {fecha} {hora}";

    private const int MaxLength = 150;

    [GeneratedRegex(@"\{n(?::(0+))?\}")]
    private static partial Regex CounterToken();

    /// <summary>
    /// Fills in the template: {fecha} is 2026-11-07, {hora} is 14-30-05, {n} is the counter and {n:000} the counter padded with zeros.
    /// The result is a valid file name without an extension; a blank template gives the default one.
    /// </summary>
    public static string Expand(string? template, DateTime now, int counter)
    {
        string name = string.IsNullOrWhiteSpace(template) ? Default : template;
        name = name
            .Replace("{fecha}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{hora}", now.ToString("HH-mm-ss"), StringComparison.OrdinalIgnoreCase);
        name = CounterToken().Replace(name, match => counter.ToString(new string('0', Math.Max(1, match.Groups[1].Length))));
        return Sanitize(name);
    }

    /// <summary>Takes out what Windows does not allow in a file name, and keeps it a sensible length.</summary>
    public static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }
        name = Regex.Replace(name, @"\s+", " ").Trim().TrimEnd('.');
        if (name.Length > MaxLength)
        {
            name = name[..MaxLength].TrimEnd();
        }
        return name.Length == 0 ? "Zarpazo" : name;
    }

    /// <summary>The path for a new file in the folder: the name as it is, or with " (2)", " (3)"… if something is already there.</summary>
    public static string UniquePath(string folder, string name, string extension)
    {
        string path = Path.Combine(folder, name + extension);
        for (int copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(folder, $"{name} ({copy}){extension}");
        }
        return path;
    }
}
