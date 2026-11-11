using System.Reflection;

namespace Zarpazo.Update;

/// <summary>The version of this copy of Zarpazo, and a tolerant reader for version texts such as a release tag.</summary>
internal static class AppVersion
{
    /// <summary>The version in Zarpazo.csproj; the "+commit" suffix the build adds is ignored.</summary>
    public static Version Current { get; } = Read();

    /// <summary>Reads "0.3.0", "v0.3.0", "0.3.0+abc123" or "0.3.0-beta". False for anything that isn't a version.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string core = text.Trim().TrimStart('v', 'V');
        int suffix = core.IndexOfAny(['+', '-']);
        if (suffix >= 0)
        {
            core = core[..suffix];
        }
        if (!Version.TryParse(core, out Version? parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>
    /// True when <paramref name="lastRun"/> is a version older than <paramref name="current"/>: Zarpazo was updated since it last
    /// ran. A first run (nothing remembered), the same version, a newer one (a downgrade) or text that isn't a version are not.
    /// </summary>
    public static bool IsUpdateFrom(string? lastRun, Version current) =>
        TryParse(lastRun, out Version previous) && current > previous;

    private static Version Read()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return TryParse(informational, out Version version) ? version : assembly.GetName().Version ?? new Version(0, 0);
    }
}
