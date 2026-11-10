using System.Diagnostics;
using Microsoft.Win32;

namespace Zarpazo.Shell;

/// <summary>
/// Windows can send Print Screen to its own Snipping Tool ("Use the Print screen button to open screen capture"),
/// which then opens next to, or instead of, Zarpazo even when the shortcut registered fine.
/// </summary>
internal static class PrintScreenConflict
{
    public const string SettingsUri = "ms-settings:easeofaccess-keyboard";

    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";

    /// <summary>True if the binding is Print Screen on its own and Windows has it assigned to the Snipping Tool.</summary>
    public static bool Applies(HotKeyBinding binding) =>
        binding is { Modifiers: 0, VirtualKey: HotKeyBinding.VkSnapshot } && WindowsOwnsPrintScreen();

    public static void OpenWindowsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(SettingsUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No settings page to open on this Windows; the written instructions are still shown.
        }
    }

    private static bool WindowsOwnsPrintScreen()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is 1; // missing on older Windows versions: nothing to warn about
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
