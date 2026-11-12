using System.IO;
using Microsoft.Win32;

namespace Zarpazo.Update;

/// <summary>
/// Tells whether this running copy is the one the installer put on the PC. Only that copy updates itself, by running the
/// installer; a loose .exe or a development build would be left behind, so those send the person to the download page.
/// </summary>
internal static class InstalledCopy
{
    // The installer's AppId plus "_is1": where Inno Setup records a per-user installation (see installer\Zarpazo.iss).
    private static readonly string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\"
        + (TestOverrides.Get("APPID") ?? "{6F0B6A52-3C1D-4E8A-9B47-5D2A0C7E91F4}") + "_is1";

    public static bool IsThisCopyInstalled() => Matches(Environment.ProcessPath, ReadInstallLocation());

    /// <summary>True when the program at <paramref name="processPath"/> sits in <paramref name="installLocation"/>.</summary>
    internal static bool Matches(string? processPath, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(processPath);
        return directory != null && string.Equals(
            directory.TrimEnd('\\', '/'), installLocation.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadInstallLocation()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
