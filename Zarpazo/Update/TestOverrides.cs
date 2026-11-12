using System.IO;

namespace Zarpazo.Update;

/// <summary>
/// Lets tools\test-update.ps1 point a test copy of Zarpazo at a local release feed and at folders of its own, so the whole
/// update (look, download, silent install, restart) can be run without touching the real installation, settings or GitHub.
/// The values come from ZARPAZO_TEST_* environment variables and are only read in a build made with -p:UpdateTest=true; every
/// normal build, and everything that is published, ignores them, so nothing can redirect where updates come from.
/// </summary>
internal static class TestOverrides
{
    /// <summary>The value of ZARPAZO_TEST_<paramref name="name"/> in a test build; always null in any other build.</summary>
    public static string? Get(string name)
    {
#if UPDATE_TEST
        string? value = Environment.GetEnvironmentVariable("ZARPAZO_TEST_" + name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
#else
        return null;
#endif
    }

    /// <summary>A folder or file of the app's data: under the test folder in a test build, else <paramref name="normal"/>.</summary>
    public static string Data(string normal, params string[] inTestFolder) =>
        Get("DATA") is { } root ? Path.Combine([root, .. inTestFolder]) : normal;
}
