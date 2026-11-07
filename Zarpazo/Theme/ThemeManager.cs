using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Zarpazo.Theme;

/// <summary>
/// Keeps the light or dark colours loaded into the application's resources. Windows refer to the colours with
/// DynamicResource, so swapping the dictionary here restyles every open window at once.
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _dictionary;
    private static bool _watching;

    /// <summary>Raised on the UI thread after the colours changed.</summary>
    public static event Action? Changed;

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>Whether the dark colours are the ones in use right now.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>The colours a mode ends up with, given what Windows is set to.</summary>
    public static bool Resolve(ThemeMode mode, bool systemIsDark) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => systemIsDark,
    };

    /// <summary>Loads the colours for the mode. Safe to call again whenever the setting changes.</summary>
    public static void Apply(ThemeMode mode)
    {
        Mode = mode;
        if (!_watching && Application.Current != null)
        {
            _watching = true;
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        }
        Refresh();
    }

    /// <summary>True when Windows is set to dark for apps. Light when the setting can't be read (older Windows 10 builds).</summary>
    public static bool SystemUsesDarkApps()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Raised on a thread of its own, so the work goes back to the UI thread.
    private static void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && Mode == ThemeMode.System)
        {
            Application.Current?.Dispatcher.BeginInvoke(Refresh);
        }
    }

    private static void Refresh()
    {
        bool dark = Resolve(Mode, SystemUsesDarkApps());
        if (dark == IsDark && _dictionary != null)
        {
            return;
        }

        IsDark = dark;
        if (Application.Current != null)
        {
            var colours = new ResourceDictionary { Source = new Uri($"/Zarpazo;component/Theme/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (_dictionary != null)
            {
                merged.Remove(_dictionary);
            }
            merged.Add(colours);
            _dictionary = colours;
        }
        Changed?.Invoke();
    }
}
