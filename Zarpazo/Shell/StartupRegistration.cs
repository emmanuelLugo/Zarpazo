using Microsoft.Win32;

namespace Zarpazo.Shell;

/// <summary>Starts the app at logon through the current user's Run key, so it needs no admin rights.</summary>
/// <param name="runKeyPath">Only the tests change this, to avoid touching the real startup list.</param>
internal sealed class StartupRegistration(
    string runKeyPath = StartupRegistration.RunKeyPath,
    string valueName = "Zarpazo",
    string? exePath = null)
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Quoted, because the path can have spaces. ProcessPath is the real .exe, also when published as a single file.
    private string Command =>
        $"\"{exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("The program's path is unknown.")}\"";

    /// <summary>True only if the entry points at this very .exe; an entry left by another copy counts as off.</summary>
    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return key?.GetValue(valueName) as string == Command;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (enabled)
        {
            key.SetValue(valueName, Command);
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
