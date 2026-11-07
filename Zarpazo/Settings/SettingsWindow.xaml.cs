using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Zarpazo.Editor;
using Zarpazo.Shell;

namespace Zarpazo.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly StartupRegistration _startup;
    private readonly Func<HotKeyBinding, bool> _tryRegisterHotKey;
    private readonly Func<HotKeyBinding, bool> _tryRegisterRecordHotKey;

    /// <param name="tryRegisterHotKey">Makes the combination the active capture shortcut; false if another program owns it.</param>
    /// <param name="tryRegisterRecordHotKey">The same for the recording shortcut.</param>
    internal SettingsWindow(SettingsStore store, StartupRegistration startup, Func<HotKeyBinding, bool> tryRegisterHotKey,
        Func<HotKeyBinding, bool> tryRegisterRecordHotKey)
    {
        InitializeComponent();
        _store = store;
        _startup = startup;
        _tryRegisterHotKey = tryRegisterHotKey;
        _tryRegisterRecordHotKey = tryRegisterRecordHotKey;

        AppSettings settings = store.Current;
        CaptureHotKey.DefaultValue = HotKeyBinding.Default;
        CaptureHotKey.Value = settings.HotKey;
        RecordHotKey.DefaultValue = HotKeyBinding.DefaultRecord;
        RecordHotKey.Value = settings.RecordHotKey;
        CountdownBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.CountdownChoices, settings.RecordCountdownSeconds));
        AudioOffsetBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.AudioOffsetChoices, settings.RecordAudioOffsetMs));
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

    // IsCancel only closes windows shown with ShowDialog; this one is modeless, so it has to close itself (Esc too).
    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        AppSettings settings = _store.Current;

        if (CaptureHotKey.Value == RecordHotKey.Value)
        {
            MessageBox.Show(this, "El atajo de captura y el de grabación no pueden ser el mismo.", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (CaptureHotKey.Value != settings.HotKey && !_tryRegisterHotKey(CaptureHotKey.Value))
        {
            MessageBox.Show(this, $"{CaptureHotKey.Value.Describe()} ya la usa otra aplicación o Windows. Elige otra combinación.",
                "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (RecordHotKey.Value != settings.RecordHotKey && !_tryRegisterRecordHotKey(RecordHotKey.Value))
        {
            MessageBox.Show(this, $"{RecordHotKey.Value.Describe()} ya la usa otra aplicación o Windows. Elige otra combinación.",
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

        settings.HotKey = CaptureHotKey.Value;
        settings.RecordHotKey = RecordHotKey.Value;
        settings.RecordCountdownSeconds = AppSettings.CountdownChoices[Math.Max(0, CountdownBox.SelectedIndex)];
        settings.RecordAudioOffsetMs = AppSettings.AudioOffsetChoices[Math.Max(0, AudioOffsetBox.SelectedIndex)];
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
