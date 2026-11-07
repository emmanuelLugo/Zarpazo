using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Zarpazo.Editor;
using Zarpazo.Shell;

namespace Zarpazo.Settings;

public partial class SettingsWindow : Window
{
    private const string DefaultHotKeyHint = "Haz clic en el cuadro y pulsa la combinación que quieras.";

    private readonly SettingsStore _store;
    private readonly StartupRegistration _startup;
    private readonly Func<HotKeyBinding, bool> _tryRegisterHotKey;
    private HotKeyBinding _hotKey;

    /// <param name="tryRegisterHotKey">Makes the combination the active shortcut; false if another program owns it.</param>
    internal SettingsWindow(SettingsStore store, StartupRegistration startup, Func<HotKeyBinding, bool> tryRegisterHotKey)
    {
        InitializeComponent();
        _store = store;
        _startup = startup;
        _tryRegisterHotKey = tryRegisterHotKey;

        AppSettings settings = store.Current;
        _hotKey = settings.HotKey;
        HotKeyBox.Text = _hotKey.Describe();
        HotKeyHint.Text = DefaultHotKeyHint;
        StartupBox.IsChecked = startup.IsEnabled;
        CopyAsFileBox.IsChecked = settings.CopyAsFile;
        FolderBox.Text = settings.SaveFolder;
        FormatBox.SelectedIndex = settings.SaveFormat == "jpg" ? 1 : 0;
        RememberStyleBox.IsChecked = settings.RememberStyle;
        KeepHistoryBox.IsChecked = settings.KeepHistory;

        // The version has a "+commit" suffix when built from git; that part means nothing to the person.
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = $"Versión {version.Split('+')[0]}";
        SettingsPathText.Text = store.FilePath;
    }

    private void OnHotKeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        CaptureHotKey(e.Key == Key.System ? e.SystemKey : e.Key);
    }

    // Windows reports Print Screen only when the key is released.
    private void OnHotKeyKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Snapshot)
        {
            e.Handled = true;
            CaptureHotKey(Key.Snapshot);
        }
    }

    private void CaptureHotKey(Key key)
    {
        if (key is Key.None or Key.DeadCharProcessed or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // a modifier on its own isn't a shortcut yet
        }

        HotKeyBinding binding = HotKeyBinding.From(key, Keyboard.Modifiers);
        if (!binding.IsAcceptable)
        {
            HotKeyHint.Text = "Usa Ctrl, Alt o Win junto con la tecla. Solo Impr Pant y F1 a F24 pueden ir solas.";
            return;
        }

        _hotKey = binding;
        HotKeyBox.Text = binding.Describe();
        HotKeyHint.Text = DefaultHotKeyHint;
    }

    private void OnResetHotKey(object sender, RoutedEventArgs e)
    {
        _hotKey = HotKeyBinding.Default;
        HotKeyBox.Text = _hotKey.Describe();
        HotKeyHint.Text = DefaultHotKeyHint;
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Carpeta donde se abre el cuadro de guardar",
            SelectedPath = Directory.Exists(FolderBox.Text) ? FolderBox.Text : "",
        };
        if (dialog.ShowDialog(new Win32Owner(new WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
        {
            FolderBox.Text = dialog.SelectedPath;
        }
    }

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(_store.FilePath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        AppSettings settings = _store.Current;

        if (_hotKey != settings.HotKey && !_tryRegisterHotKey(_hotKey))
        {
            MessageBox.Show(this, $"{_hotKey.Describe()} ya la usa otra aplicación o Windows. Elige otra combinación.",
                "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (StartupBox.IsChecked != _startup.IsEnabled)
            {
                _startup.SetEnabled(StartupBox.IsChecked == true);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // A policy can block the Run key. The rest of the settings still apply.
            MessageBox.Show(this, "No se pudo cambiar el inicio con Windows. Es posible que una directiva del sistema lo bloquee.",
                "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        settings.HotKey = _hotKey;
        settings.CopyAsFile = CopyAsFileBox.IsChecked == true;
        settings.SaveFolder = FolderBox.Text.Trim();
        settings.SaveFormat = FormatBox.SelectedIndex == 1 ? "jpg" : "png";
        settings.RememberStyle = RememberStyleBox.IsChecked == true;
        settings.KeepHistory = KeepHistoryBox.IsChecked == true;

        try
        {
            _store.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"No se pudo guardar la configuración: {ex.Message}", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Close();
    }
}
