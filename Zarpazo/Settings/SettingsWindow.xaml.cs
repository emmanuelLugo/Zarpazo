using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Zarpazo.Editor;
using Zarpazo.Recording;
using Zarpazo.Shell;
using Zarpazo.Theme;
using Zarpazo.Update;

namespace Zarpazo.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly StartupRegistration _startup;
    private readonly Func<HotKeyBinding, bool> _tryRegisterHotKey;
    private readonly Func<HotKeyBinding, bool> _tryRegisterRecordHotKey;
    private readonly Func<HotKeyBinding, bool>? _tryRegisterGroupHotKey;
    private readonly UpdateService? _updates;
    private CancellationTokenSource? _download;

    /// <param name="tryRegisterHotKey">Makes the combination the active capture shortcut; false if another program owns it.</param>
    /// <param name="tryRegisterRecordHotKey">The same for the recording shortcut.</param>
    /// <param name="updates">Looks for and installs new versions; without it the update section is hidden.</param>
    /// <param name="showUpdates">Opens on the General page, where the update is.</param>
    internal SettingsWindow(SettingsStore store, StartupRegistration startup, Func<HotKeyBinding, bool> tryRegisterHotKey,
        Func<HotKeyBinding, bool> tryRegisterRecordHotKey, UpdateService? updates = null, bool showUpdates = false,
        Func<HotKeyBinding, bool>? tryRegisterGroupHotKey = null)
    {
        InitializeComponent();
        TitleBarTheme.Follow(this);
        _store = store;
        _startup = startup;
        _tryRegisterHotKey = tryRegisterHotKey;
        _tryRegisterRecordHotKey = tryRegisterRecordHotKey;
        _tryRegisterGroupHotKey = tryRegisterGroupHotKey;

        AppSettings settings = store.Current;
        CaptureHotKey.DefaultValue = HotKeyBinding.Default;
        CaptureHotKey.ValueChanged += ShowPrintScreenNoticeIfNeeded;
        GroupHotKey.DefaultValue = HotKeyBinding.DefaultGroup;
        RecordHotKey.DefaultValue = HotKeyBinding.DefaultRecord;
        StartupBox.IsChecked = startup.IsEnabled; // not part of AppSettings, so "Restablecer" leaves it alone
        LoadGeneral(settings);
        LoadHotKeys(settings);
        LoadCapture(settings);
        LoadRecording(settings);
        LoadSaving(settings);

        // The version has a "+commit" suffix when built from git; that part means nothing to the person.
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = $"Versión {version.Split('+')[0]}";
        SettingsPathText.Text = store.FilePath;

        _updates = updates;
        UpdatePanel.Visibility = updates == null ? Visibility.Collapsed : Visibility.Visible;
        if (updates != null)
        {
            updates.Changed += ShowUpdateState;
            Closed += (_, _) =>
            {
                updates.Changed -= ShowUpdateState;
                _download?.Cancel();
            };
            ShowUpdateState();
        }
        ShowPage(0);
    }

    internal void ShowUpdates() => Nav.SelectedIndex = 0;

    // Each page has its own Load method, so "Restablecer esta sección" can fill it with the factory values.
    private void LoadGeneral(AppSettings settings)
    {
        ThemeBox.SelectedIndex = (int)settings.AppearanceMode;
        AutoUpdateBox.IsChecked = settings.CheckForUpdates;
        BackgroundDownloadBox.IsChecked = settings.DownloadUpdatesInBackground;
    }

    private void LoadHotKeys(AppSettings settings)
    {
        CaptureHotKey.Value = settings.HotKey;
        RecordHotKey.Value = settings.RecordHotKey;
        GroupHotKey.Value = settings.GroupHotKey;
    }

    private void LoadCapture(AppSettings settings)
    {
        MagnifierBox.IsChecked = settings.ShowMagnifier;
        RememberStyleBox.IsChecked = settings.RememberStyle;
        CopyAsFileBox.IsChecked = settings.CopyAsFile;
        GroupOneImageBox.IsChecked = settings.GroupCopyAsOneImage;
        GroupNumberingBox.IsChecked = settings.GroupNumbering;
    }

    private void LoadRecording(AppSettings settings)
    {
        CountdownBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.CountdownChoices, settings.RecordCountdownSeconds));
        AudioOffsetBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.AudioOffsetChoices, settings.RecordAudioOffsetMs));
        RecordingQuality quality = settings.RecordingQuality;
        GifFpsBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingQuality.GifFramesPerSecondChoices, quality.GifFramesPerSecond));
        GifMaxBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingQuality.GifMaxSecondsChoices, quality.GifMaxSeconds));
        GifDitherBox.IsChecked = quality.GifDither;
        VideoFpsBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingQuality.VideoFramesPerSecondChoices, quality.VideoFramesPerSecond));
        VideoQualityBox.SelectedIndex = (int)quality.VideoQuality;
        VideoMaxBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingQuality.VideoMaxMinutesChoices, quality.VideoMaxMinutes));
    }

    private void LoadSaving(AppSettings settings)
    {
        AutoSaveBox.IsChecked = settings.AutoSave;
        AutoFolderBox.Text = settings.AutoSaveFolder;
        TemplateBox.Text = settings.FileNameTemplate;
        FolderBox.Text = settings.SaveFolder;
        FormatBox.SelectedIndex = settings.SaveFormat == "jpg" ? 1 : 0;
        JpegQualityBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.JpegQualityChoices, settings.JpegQuality));
        KeepHistoryBox.IsChecked = settings.KeepHistory;
        HistoryAgeBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.HistoryMaxAgeChoices, settings.HistoryMaxAgeDays));
    }

    private void OnPageSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PageAbout != null) // it fires while the window is still being built; the last page is the last thing to exist
        {
            ShowPage(Nav.SelectedIndex);
        }
    }

    private void ShowPage(int index)
    {
        UIElement[] pages = [PageGeneral, PageHotKeys, PageCapture, PageRecording, PageSaving, PageAbout];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
        ResetPageButton.Visibility = index == pages.Length - 1 ? Visibility.Collapsed : Visibility.Visible;
        PageScroll.ScrollToTop();
    }

    // Only the controls change; nothing is saved until Aceptar, so Cancelar still undoes it.
    private void OnResetPage(object sender, RoutedEventArgs e)
    {
        var factory = new AppSettings();
        switch (Nav.SelectedIndex)
        {
            case 0: LoadGeneral(factory); break;
            case 1: LoadHotKeys(factory); break;
            case 2: LoadCapture(factory); break;
            case 3: LoadRecording(factory); break;
            case 4: LoadSaving(factory); break;
        }
    }

    // What the update section says and offers, from what the last check found.
    private void ShowUpdateState()
    {
        if (_updates?.Available is { } release)
        {
            bool inPlace = _updates.CanInstallInPlace;
            bool ready = inPlace && _updates.Ready != null;
            InstallUpdateButton.Content = !inPlace ? "Abrir la página de descarga" : ready ? "Reiniciar y actualizar" : "Descargar e instalar";
            InstallUpdateButton.Visibility = NotesButton.Visibility = Visibility.Visible;
            InstallLaterButton.Visibility = ready && !_updates.InstallOnClose ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus.Text = $"Hay una versión nueva: {release.Version} (tienes la {AppVersion.Current})."
                + (!inPlace ? " Esta copia no se instaló con el instalador, así que se descarga desde la página."
                    : ready && _updates.InstallOnClose ? " Está descargada y se instalará al salir de Zarpazo, o la próxima vez que la abras."
                    : ready ? " Ya está descargada y comprobada: al reiniciar se instala sola y Zarpazo vuelve a abrirse."
                    : _updates.IsDownloading ? " Descargándola en segundo plano…" : "");
        }
        else
        {
            InstallUpdateButton.Visibility = InstallLaterButton.Visibility = NotesButton.Visibility = Visibility.Collapsed;
            DateTime last = _store.Current.LastUpdateCheck;
            UpdateStatus.Text = last > DateTime.MinValue
                ? $"Tienes la última versión. Última comprobación: {last.ToLocalTime():g}."
                : "";
        }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (_updates == null)
        {
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Buscando…";
        UpdateCheck result = await _updates.CheckAsync();
        CheckUpdateButton.IsEnabled = true;
        if (result.IsFailure)
        {
            UpdateStatus.Text = result.Error!;
        }
        else
        {
            ShowUpdateState();
        }
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_updates?.Available is not { } release)
        {
            return;
        }
        if (!_updates.CanInstallInPlace)
        {
            OnOpenReleasePage(sender, e);
            return;
        }

        bool downloaded = _updates.Ready != null;
        InstallUpdateButton.IsEnabled = InstallLaterButton.IsEnabled = CheckUpdateButton.IsEnabled = false;
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = downloaded ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatus.Text = downloaded ? "Reiniciando para actualizar…" : $"Descargando la versión {release.Version}…";
        _download = new CancellationTokenSource();
        try
        {
            await _updates.InstallAsync(new Progress<double>(done => UpdateProgress.Value = done), _download.Token);
            UpdateStatus.Text = "Instalando. Zarpazo se cierra y se abre de nuevo solo."; // the app is closing now
        }
        catch (UpdateException ex)
        {
            UpdateStatus.Text = ex.Message;
            InstallUpdateButton.IsEnabled = InstallLaterButton.IsEnabled = CheckUpdateButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // the window was closed while downloading
        }
    }

    // Keeps the downloaded update for when Zarpazo closes (or starts again), instead of restarting right now.
    private void OnInstallLater(object sender, RoutedEventArgs e)
    {
        if (_updates?.Ready != null)
        {
            _updates.InstallOnClose = true; // raises Changed, which redraws this section
        }
    }

    private void OnOpenReleasePage(object sender, RoutedEventArgs e)
    {
        if (_updates?.Available is { } release)
        {
            Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
        }
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

    private void OnBrowseAutoFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Carpeta donde se guardan solas las capturas y grabaciones",
            SelectedPath = Directory.Exists(AutoFolderBox.Text) ? AutoFolderBox.Text : "",
        };
        if (dialog.ShowDialog(new Win32Owner(new WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
        {
            AutoFolderBox.Text = dialog.SelectedPath;
        }
    }

    // Shows what the next file would be called, so the template can be tried out before saving.
    private void OnTemplateChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TemplatePreview != null)
        {
            TemplatePreview.Text = $"Ejemplo: {Output.FileNameTemplate.Expand(TemplateBox.Text, DateTime.Now, _store.Current.AutoSaveCounter + 1)}.png";
        }
    }

    private void OnDefaultTemplate(object sender, RoutedEventArgs e) => TemplateBox.Text = Output.FileNameTemplate.Default;

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(_store.FilePath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void ShowPrintScreenNoticeIfNeeded() =>
        PrintScreenNotice.Visibility = PrintScreenConflict.Applies(CaptureHotKey.Value) ? Visibility.Visible : Visibility.Collapsed;

    private void OnOpenWindowsKeyboardSettings(object sender, RoutedEventArgs e) => PrintScreenConflict.OpenWindowsSettings();

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
        if (GroupHotKey.Value == CaptureHotKey.Value || GroupHotKey.Value == RecordHotKey.Value)
        {
            MessageBox.Show(this, "El atajo de las capturas agrupadas no puede ser el mismo que el de captura o el de grabación.", "Zarpazo", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (_tryRegisterGroupHotKey != null && GroupHotKey.Value != settings.GroupHotKey && !_tryRegisterGroupHotKey(GroupHotKey.Value))
        {
            MessageBox.Show(this, $"{GroupHotKey.Value.Describe()} ya la usa otra aplicación o Windows. Elige otra combinación.",
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

        settings.AppearanceMode = (ThemeMode)Math.Max(0, ThemeBox.SelectedIndex);
        settings.CheckForUpdates = AutoUpdateBox.IsChecked == true;
        settings.DownloadUpdatesInBackground = BackgroundDownloadBox.IsChecked == true;
        settings.HotKey = CaptureHotKey.Value;
        settings.RecordHotKey = RecordHotKey.Value;
        settings.GroupHotKey = GroupHotKey.Value;
        settings.GroupCopyAsOneImage = GroupOneImageBox.IsChecked == true;
        settings.GroupNumbering = GroupNumberingBox.IsChecked == true;
        settings.ShowMagnifier = MagnifierBox.IsChecked == true;
        settings.RecordCountdownSeconds = AppSettings.CountdownChoices[Math.Max(0, CountdownBox.SelectedIndex)];
        settings.RecordAudioOffsetMs = AppSettings.AudioOffsetChoices[Math.Max(0, AudioOffsetBox.SelectedIndex)];
        settings.JpegQuality = AppSettings.JpegQualityChoices[Math.Max(0, JpegQualityBox.SelectedIndex)];
        settings.RecordingQuality = new RecordingQuality(
            RecordingQuality.GifFramesPerSecondChoices[Math.Max(0, GifFpsBox.SelectedIndex)],
            GifDitherBox.IsChecked == true,
            RecordingQuality.GifMaxSecondsChoices[Math.Max(0, GifMaxBox.SelectedIndex)],
            RecordingQuality.VideoFramesPerSecondChoices[Math.Max(0, VideoFpsBox.SelectedIndex)],
            (VideoQuality)Math.Max(0, VideoQualityBox.SelectedIndex),
            RecordingQuality.VideoMaxMinutesChoices[Math.Max(0, VideoMaxBox.SelectedIndex)]);
        settings.CopyAsFile = CopyAsFileBox.IsChecked == true;
        settings.SaveFolder = FolderBox.Text.Trim();
        settings.SaveFormat = FormatBox.SelectedIndex == 1 ? "jpg" : "png";
        settings.RememberStyle = RememberStyleBox.IsChecked == true;
        settings.KeepHistory = KeepHistoryBox.IsChecked == true;
        settings.HistoryMaxAgeDays = AppSettings.HistoryMaxAgeChoices[Math.Max(0, HistoryAgeBox.SelectedIndex)];
        settings.AutoSave = AutoSaveBox.IsChecked == true;
        settings.AutoSaveFolder = AutoFolderBox.Text.Trim();
        settings.FileNameTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text) ? Output.FileNameTemplate.Default : TemplateBox.Text.Trim();

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
