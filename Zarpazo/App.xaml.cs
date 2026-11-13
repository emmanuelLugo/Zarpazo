using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zarpazo.Capture;
using Zarpazo.Guide;
using Zarpazo.History;
using Zarpazo.Overlay;
using Zarpazo.Recording;
using Zarpazo.Settings;
using Zarpazo.Shell;
using Zarpazo.Theme;
using Zarpazo.Update;
using Application = System.Windows.Application;

namespace Zarpazo;

public partial class App : Application
{
    private static readonly string InstanceMutexName = TestOverrides.Get("MUTEX") ?? @"Local\Zarpazo.SingleInstance";
    private const int MenuCloseDelayMs = 200;

    private Mutex? _instanceMutex;
    private TrayIcon? _tray;
    private GlobalHotKey? _hotKey;
    private GlobalHotKey? _recordHotKey;
    private GlobalHotKey? _groupHotKey;
    private Rectangle? _lastRecordingRegion; // what the recording shortcut repeats
    private Rectangle? _lastArea; // the last area copied, saved, pinned or recorded, for "Repetir la última zona"
    private bool _delayPending; // a delayed capture is counting down, or about to show its overlay
    private bool _autoRestartStarted;
    private bool _recordingStarting; // the area was chosen and the recording is about to begin

    // Anything a restart would cut short: choosing an area or editing a capture (the overlay), a recording from the countdown until its
    // bar is closed (which covers encoding the video and the video editor), and the moments in between.
    private bool IsBusy => _overlay != null || _recording != null || _delayPending || _recordingStarting || _openEditors > 0;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private GuideWindow? _guideWindow;
    private readonly GuideDocument _guide = new(GuideDocument.DefaultFolder);
    private RecordingSession? _recording;
    private int _openEditors; // finished videos being edited: the app is busy until they close
    private readonly CaptureHistory _history = new(CaptureHistory.DefaultFolder);
    private SettingsStore _settings = null!; // created in OnStartup, before anything can use it
    private UpdateService _updates = null!; // the same
    private DispatcherTimer? _updateTimer;
    private UpdateToast? _updateToast; // the card about a version that is ready
    private UpdateToast? _updatedToast; // the card that says Zarpazo was just updated
    private Version? _justUpdatedTo; // set on a start after an update, until the card has been shown

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _settings = new SettingsStore(SettingsStore.DefaultPath);
        try
        {
            _history.DeleteOlderThan(_settings.Current.HistoryMaxAgeDays, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder is not reachable right now; the history is cleaned the next time.
        }
        _tray = new TrayIcon(CaptureFromMenu, CaptureFullScreen, RepeatLastArea, () => _lastArea != null, CaptureAfterDelay,
            ShowHistory, ShowGuide, () => _guide.AddsOnCapture = !_guide.AddsOnCapture, () => ShowSettings(), () => _recording?.Stop(), ExitFromMenu);
        ThemeManager.Changed += () => _tray.SetDark(ThemeManager.IsDark);
        ThemeManager.Apply(_settings.Current.AppearanceMode);

        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // each request sets its own limit
        _updates = new UpdateService(_settings, new UpdateChecker(http, AppVersion.Current),
            new UpdateDownloader(http, UpdateDownloader.DefaultFolder), LaunchInstaller, isBusy: () => IsBusy);
        _updates.Changed += OnUpdateChanged;
        _tray.UpdateRequested += () => ShowSettings(showAbout: true);
        _tray.RestartForUpdateRequested += ApplyUpdateNow;
        _tray.CheckUpdatesRequested += async () => await CheckForUpdatesNowAsync();
        AnnounceUpdateIfNew();
        StartUpdateChecks();
        _hotKey = new GlobalHotKey(() => ShowOverlay());
        if (!_hotKey.TryRegister(_settings.Current.HotKey))
        {
            _tray.ShowWarning($"Otra aplicación ya usa {_settings.Current.HotKey.Describe()}. Puedes cambiar el atajo en Configuración o capturar desde este icono.");
        }
        else if (PrintScreenConflict.Applies(_settings.Current.HotKey))
        {
            _tray.ShowWarning("Windows tiene Impr Pant asignada al Recortes y puede abrirlo en vez de Zarpazo. Desactiva \"Usar el botón Impr Pant para abrir la captura de pantalla\" en Configuración de Windows, o cambia el atajo en Zarpazo.");
        }
        _guide.Changed += () => _tray!.SetGroupMode(_guide.AddsOnCapture, _guide.Steps.Count);
        _tray.SetGroupMode(false, _guide.Steps.Count);
        _groupHotKey = new GlobalHotKey(CopyGroupFromHotKey);
        if (!_groupHotKey.TryRegister(_settings.Current.GroupHotKey))
        {
            _tray.ShowWarning($"Otra aplicación ya usa {_settings.Current.GroupHotKey.Describe()}, el atajo de las capturas agrupadas. Puedes cambiarlo en Configuración.");
        }
        _recordHotKey = new GlobalHotKey(RecordFromHotKey);
        if (!_recordHotKey.TryRegister(_settings.Current.RecordHotKey))
        {
            _tray.ShowWarning($"Otra aplicación ya usa {_settings.Current.RecordHotKey.Describe()}, el atajo de grabación. Puedes cambiarlo en Configuración.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop();
        _hotKey?.Dispose();
        _recordHotKey?.Dispose();
        _groupHotKey?.Dispose();
        _tray?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private async void CaptureFromMenu()
    {
        // The tray menu is still on screen for a moment after the click and would end up in the capture.
        await Task.Delay(MenuCloseDelayMs);
        ShowOverlay();
    }

    // The whole monitor the pointer is on, already chosen: Enter copies it, or the bars can be used first.
    private async void CaptureFullScreen()
    {
        await Task.Delay(MenuCloseDelayMs);
        ShowOverlay(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).Bounds);
    }

    // The same place again, on a fresh picture of the screen: for taking the same shot of something that changes.
    private async void RepeatLastArea()
    {
        await Task.Delay(MenuCloseDelayMs);
        ShowOverlay(_lastArea);
    }

    // Counts down on the monitor the pointer is on, so a menu can be opened or something hovered, then captures.
    private async void CaptureAfterDelay(int seconds)
    {
        if (_delayPending || _overlay != null || _recording is { IsRecording: true } or { IsCountingDown: true })
        {
            return;
        }

        _delayPending = true; // kept until the overlay is up, so there is no moment in between when nothing seems to be going on
        try
        {
            var countdown = new RecordingCountdown(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).Bounds);
            try
            {
                await Task.Delay(MenuCloseDelayMs);
                countdown.Show();
                for (int left = seconds; left > 0 && _overlay == null; left--) // the shortcut during the wait captures right away
                {
                    countdown.ShowNumber(left);
                    await Task.Delay(1000);
                }
            }
            finally
            {
                countdown.Close();
            }

            await Task.Delay(MenuCloseDelayMs / 2); // the number is excluded from the picture, but older Windows versions ignore that
            ShowOverlay();
        }
        finally
        {
            _delayPending = false;
            OnMaybeIdle();
        }
    }

    private void ShowGuide()
    {
        if (_guideWindow != null)
        {
            _guideWindow.Activate();
            return;
        }

        _guideWindow = new GuideWindow(_guide, _settings);
        _guideWindow.Closed += (_, _) => _guideWindow = null;
        _guideWindow.Show();
    }

    private void AddGuideStep(BitmapSource image)
    {
        GuideStep step = _guide.Add(image); // a failure here is shown by the overlay, which then stays open
        _guide.AddsOnCapture = true; // the first capture added starts the group mode: the next ones only need Enter
        _tray!.ShowInfo($"Añadida al grupo (captura {step.Number})");
    }

    // The group shortcut: everything in the group goes to the clipboard, ready to paste, and the group starts again empty.
    private void CopyGroupFromHotKey()
    {
        if (_guide.Steps.Count == 0)
        {
            _tray!.ShowInfo("No hay capturas en el grupo");
            return;
        }

        try
        {
            int count = GuideShare.Copy(_guide.Steps, _settings.Current);
            _guide.Clear();
            _tray!.ShowInfo($"{count} {(count == 1 ? "captura copiada" : "capturas copiadas")}: pega donde quieras");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _tray!.ShowWarning($"No se pudo copiar el grupo: {ex.Message}");
        }
    }

    private void ShowHistory()
    {
        if (_historyWindow != null)
        {
            _historyWindow.Activate();
            return;
        }

        _historyWindow = new HistoryWindow(_history, _settings, EditFromHistory);
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show();
    }

    // Opens an earlier capture in the overlay: the old image goes back where it was on a fresh picture of the screen.
    private async void EditFromHistory(string path)
    {
        if (_overlay != null)
        {
            return;
        }

        if (_history.LoadEditable(path) is not (var state, var image))
        {
            _tray!.ShowWarning("Esta captura no se puede abrir para editar.");
            return;
        }

        // Out of the way, or it would be part of the picture of the screen.
        _historyWindow?.Hide();
        await Task.Delay(MenuCloseDelayMs);

        try
        {
            List<Rectangle> windows = WindowFinder.GetVisibleWindowBounds();
            (BitmapSource screen, Rectangle bounds) = ScreenCapture.CaptureVirtualScreen();
            (BitmapSource backdrop, System.Drawing.Point origin) = EditBackdrop.Place(screen, bounds, image, state.Left, state.Top);

            _overlay = new OverlayWindow(backdrop, bounds, windows, _settings, _history, new EditRestore(state, origin.X, origin.Y, path));
            _overlay.Closed += (_, _) =>
            {
                _overlay = null;
                _historyWindow?.Reload(); // the edited capture replaced the old one
                _historyWindow?.Show();
                ReleaseMemoryWhenIdle();
                OnMaybeIdle();
            };
            _overlay.Show();
        }
        catch (Exception ex)
        {
            _historyWindow?.Show();
            _tray!.ShowWarning($"No se pudo abrir la captura: {ex.Message}");
        }
    }

    private void ShowSettings(bool showAbout = false)
    {
        if (_settingsWindow != null)
        {
            if (showAbout)
            {
                _settingsWindow.ShowUpdates();
            }
            _settingsWindow.Activate();
            return;
        }

        // While the shortcut is registered Windows keeps the key from the window, so it couldn't be recorded.
        _hotKey!.Unregister();
        _recordHotKey!.Unregister();
        _groupHotKey!.Unregister();
        _settingsWindow = new SettingsWindow(_settings, new StartupRegistration(), _hotKey.TryRegister, _recordHotKey.TryRegister, _updates, showAbout,
            _groupHotKey.TryRegister);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ThemeManager.Apply(_settings.Current.AppearanceMode);
            if (!_hotKey.TryRegister(_settings.Current.HotKey))
            {
                _tray!.ShowWarning($"No se pudo activar {_settings.Current.HotKey.Describe()}: otra aplicación lo usa.");
            }
            if (!_recordHotKey.TryRegister(_settings.Current.RecordHotKey))
            {
                _tray!.ShowWarning($"No se pudo activar {_settings.Current.RecordHotKey.Describe()}: otra aplicación lo usa.");
            }
            if (!_groupHotKey.TryRegister(_settings.Current.GroupHotKey))
            {
                _tray!.ShowWarning($"No se pudo activar {_settings.Current.GroupHotKey.Describe()}: otra aplicación lo usa.");
            }
        };
        _settingsWindow.Show();
    }

    // The first look a little after start-up, so it doesn't compete with the rest, and then every few hours: the app can
    // stay open for days, and the look only goes to GitHub when a day has passed since the last good one.
    private async void StartUpdateChecks()
    {
        try
        {
            // An update downloaded in an earlier run is ready again at once, with no network.
            await _updates.RestorePendingAsync(AppVersion.Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the pending download is checked again with the next look
        }

        // "Al cerrar" was chosen but Zarpazo was never closed from its menu (Windows shut down, the PC restarted): the next
        // start installs it, before anything else is shown.
        if (_updates.InstallOnClose && await ApplyUpdateAsync(relaunch: true))
        {
            return;
        }

        // (a test build, from tools\test-update.ps1, can shorten the wait)
        double firstLookAfter = double.TryParse(TestOverrides.Get("CHECKDELAY"), System.Globalization.CultureInfo.InvariantCulture, out double seconds) ? seconds : 30;
        await Task.Delay(TimeSpan.FromSeconds(firstLookAfter));
        await CheckForUpdatesQuietlyAsync();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(3) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesQuietlyAsync();
        _updateTimer.Start();
    }

    private async Task CheckForUpdatesQuietlyAsync()
    {
        try
        {
            if (!_updates.IsDue())
            {
                return;
            }

            await AnnounceAsync(await _updates.CheckAsync(), manual: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // a failed look or a settings file that can't be written is no reason to bother anyone
        }
    }

    // From the tray menu: looks now, whatever the last check was, and always says what it found.
    private async Task CheckForUpdatesNowAsync()
    {
        try
        {
            UpdateCheck result = await _updates.CheckAsync();
            if (result.IsFailure)
            {
                _tray!.ShowWarning("No se pudo buscar actualizaciones. Revisa tu conexión e inténtalo de nuevo.");
            }
            else if (result.Newer != null)
            {
                await AnnounceAsync(result, manual: true);
            }
            else
            {
                _tray!.ShowInfo($"Zarpazo está al día (versión {AppVersion.Current}).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _tray!.ShowWarning("No se pudo buscar actualizaciones.");
        }
    }

    // Says that a newer version exists. If it is being downloaded in the background, it waits for that and announces it once it
    // is ready (the card); a version that can't be, or couldn't be, downloaded gets the plain notice that opens the settings.
    private async Task AnnounceAsync(UpdateCheck result, bool manual)
    {
        if (result.Newer is not { } release)
        {
            return;
        }

        if (manual && _updates.IsDownloading)
        {
            _tray!.ShowInfo($"Descargando la versión {release.Version}…");
        }
        await _updates.Staged;

        if (_updates.Ready is { } ready)
        {
            // OnUpdateChanged already showed the card unless this one was announced before; asked by hand it is shown again.
            if (manual)
            {
                ShowUpdateToast(ready);
            }
            return;
        }

        // Told once per version: not again at every start-up while the person decides. By hand, always.
        if (manual || _settings.Current.UpdateNotifiedVersion != release.Tag)
        {
            RememberNotified(release);
            _tray!.ShowUpdateNotice($"Zarpazo {release.Version} está disponible. Haz clic aquí para verla.");
        }
    }

    // The tray entry follows the update, and a version that just became ready is announced with the card, once per version.
    private void OnUpdateChanged()
    {
        ReleaseInfo? ready = _updates.Ready;
        _tray!.SetUpdate(_updates.Available?.Version.ToString(), ready != null, _updates.RestartQueued);
        if (ready != null && TestOverrides.Get("AUTORESTART") == "1" && !_autoRestartStarted)
        {
            _autoRestartStarted = true; // test builds only: restart as soon as the update is ready, as if the button had been pressed
            ApplyUpdateNow();
            return;
        }

        if (ready == null)
        {
            _updateToast?.Dismiss(); // it was about a version that has been replaced
        }
        else if (_settings.Current.UpdateNotifiedVersion != ready.Tag && !IsBusy && _updatedToast == null)
        {
            ShowUpdateToast(ready); // while busy it waits: OnMaybeIdle shows it when the person is done
        }
    }

    // Called whenever something the person was doing ends. A restart they asked for and that was put off happens now, and a
    // new version that was not announced because they were busy is announced.
    private async void OnMaybeIdle()
    {
        if (IsBusy || _updates == null)
        {
            return;
        }

        try
        {
            if (await _updates.ResumeIfIdleAsync())
            {
                return; // the installer has taken over
            }
        }
        catch (UpdateException ex)
        {
            _tray?.ShowWarning(ex.Message);
        }

        if (_updates.Ready is { } ready && _settings.Current.UpdateNotifiedVersion != ready.Tag && !IsBusy && _updatedToast == null)
        {
            ShowUpdateToast(ready);
        }
        ShowJustUpdatedIfIdle();
    }

    private void ShowUpdateToast(ReleaseInfo release)
    {
        if (_updateToast != null || _updatedToast != null)
        {
            return;
        }

        RememberNotified(release);
        var toast = new UpdateToast(release.Version.ToString(), ApplyUpdateNow, InstallUpdateOnClose, () => OpenPage(release.PageUrl));
        toast.Closed += (_, _) =>
        {
            _updateToast = null;
            ShowJustUpdatedIfIdle();
        };
        _updateToast = toast;
        toast.Show();
    }

    // The first start on a newer version than the one that ran last time: Zarpazo says so once, with a way to read what changed.
    // Nothing is said on a first run, or when the version is the same or older.
    private async void AnnounceUpdateIfNew()
    {
        Version current = AppVersion.Current;
        string last = _settings.Current.LastRunVersion;
        if (AppVersion.IsUpdateFrom(last, current))
        {
            _justUpdatedTo = current;
        }

        if (last != current.ToString())
        {
            _settings.Current.LastRunVersion = current.ToString();
            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // it would be announced again at the next start, which is harmless
            }
        }

        if (_justUpdatedTo != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(4)); // let the start-up settle first
            ShowJustUpdatedIfIdle();
        }
    }

    // Not while the person is capturing or recording, and not on top of the card about another version: it waits for them to end.
    private void ShowJustUpdatedIfIdle()
    {
        if (_justUpdatedTo is not { } version || IsBusy || _updateToast != null || _updatedToast != null)
        {
            return;
        }

        _justUpdatedTo = null;
        var toast = UpdateToast.Updated(version.ToString(), () => OpenPage(ReleaseFeed.PageFor(version)));
        toast.Closed += (_, _) =>
        {
            _updatedToast = null;
            OnMaybeIdle(); // a card about a new version may have been waiting for this one to go
        };
        _updatedToast = toast;
        toast.Show();
    }

    private void RememberNotified(ReleaseInfo release)
    {
        _settings.Current.UpdateNotifiedVersion = release.Tag;
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // at worst the same version is announced once more
        }
    }

    private static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // no browser to open it with
        }
    }

    // One click: the app closes, the installer runs with no window, and Zarpazo opens again on the new version. If the person is
    // in the middle of a capture, an edit or a recording it waits until they are done, and says so.
    private async void ApplyUpdateNow()
    {
        try
        {
            if (!await _updates.RestartAsync())
            {
                _tray!.ShowInfo("Estás usando Zarpazo. Se reiniciará para actualizar en cuanto termines.");
            }
        }
        catch (UpdateException ex)
        {
            _tray!.ShowWarning(ex.Message);
        }
    }

    private void InstallUpdateOnClose()
    {
        _updates.InstallOnClose = true;
        _tray!.ShowInfo("Se instalará al salir de Zarpazo, o la próxima vez que la abras.");
    }

    // The tray's Exit: a ready update chosen for "Al cerrar" is installed on the way out, with nothing opened afterwards.
    private async void ExitFromMenu()
    {
        if (_updates.InstallOnClose && await ApplyUpdateAsync(relaunch: false, warn: false))
        {
            return; // the installer has taken over and the app is closing
        }

        Shutdown();
    }

    /// <returns>True when the installer was started and the app is closing.</returns>
    private async Task<bool> ApplyUpdateAsync(bool relaunch, bool warn = true)
    {
        try
        {
            await _updates.InstallAsync(relaunch: relaunch);
            return true;
        }
        catch (UpdateException ex)
        {
            if (warn)
            {
                _tray?.ShowWarning(ex.Message);
            }
            return false;
        }
    }

    // Runs the downloaded installer, with no window at all, and closes this copy so it can be replaced; null if all went well,
    // else why not.
    private string? LaunchInstaller(string setupPath, bool relaunch)
    {
        // Whether the person is in the middle of something was asked by the update service just before this.

        // The installer refuses to go on while it sees this mutex, so it is let go before the installer starts.
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        try
        {
            // Nothing is shown. /RELAUNCH=1: the installer opens Zarpazo again when it finishes, as a silent install has no "open" box.
            Process.Start(new ProcessStartInfo(setupPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" + (relaunch ? " /RELAUNCH=1" : "")) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out _); // still running: keep being the one instance
            return $"No se pudo iniciar el instalador: {ex.Message}";
        }

        Shutdown();
        return null;
    }

    // One key for the whole recording: it stops what is running, repeats the last area, or asks for an area the first time.
    private void RecordFromHotKey()
    {
        if (_recording is { IsRecording: true } or { IsCountingDown: true })
        {
            _recording.Stop();
        }
        else if (_overlay != null)
        {
            return; // choosing an area right now
        }
        else if (_lastRecordingRegion is { } region)
        {
            BeginRecording(region, _settings.Current.RecordingOptions);
        }
        else
        {
            ShowOverlay(); // no area yet: pick one, then press the record button
        }
    }

    // Starts recording once the overlay has gone, so it can't end up in the first pictures.
    private async void BeginRecording(Rectangle region, RecordingOptions options)
    {
        _lastRecordingRegion = region;
        _recordingStarting = true; // the overlay is about to go: from here to the session being created the app is still busy
        RecordingSession session;
        try
        {
            await Task.Delay(MenuCloseDelayMs);
            _recording?.Close(); // a finished one still on screen makes way for the new one
            session = new RecordingSession(region, options, _settings, _history, () =>
            {
                _recording = null;
                OnMaybeIdle();
            });
            _recording = session;
        }
        finally
        {
            _recordingStarting = false;
        }

        // The tray icon shows that something is being recorded, even when the bar is out of sight.
        session.StateChanged += () => _tray?.SetRecording(session.IsCountingDown || session.IsRecording, session.IsPaused);
        session.Failed += message => _tray!.ShowWarning(message);
        session.EditorOpened += editor =>
        {
            _openEditors++;
            editor.Closed += (_, _) =>
            {
                _openEditors--;
                OnMaybeIdle();
            };
        };
        try
        {
            session.Start(_settings.Current.RecordCountdownSeconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            session.Close(); // sets _recording to null and checks whether the app is idle
            _tray!.ShowWarning($"No se pudo empezar a grabar: {ex.Message}");
        }
    }

    private void ShowOverlay(Rectangle? area = null)
    {
        // The same shortcut that starts a capture stops a recording: it is the one key that is always within reach.
        if (_recording is { IsRecording: true } or { IsCountingDown: true })
        {
            _recording.Stop(); // stops a recording, or cancels the countdown
            return;
        }
        if (_overlay != null)
        {
            return;
        }

        BitmapSource image;
        Rectangle bounds;
        List<Rectangle> windows;
        try
        {
            windows = WindowFinder.GetVisibleWindowBounds(); // right before the capture, so both show the same screen
            (image, bounds) = ScreenCapture.CaptureVirtualScreen();
        }
        catch (Exception ex)
        {
            _tray!.ShowWarning($"No se pudo capturar la pantalla: {ex.Message}");
            return;
        }

        _overlay = new OverlayWindow(image, bounds, windows, _settings, _history, initialArea: area);
        _overlay.AreaUsed += used => _lastArea = used;
        _overlay.RecordRequested += BeginRecording;
        _overlay.SetGroup(_guide.AddsOnCapture, _guide.Steps.Count);
        _overlay.GuideStepRequested += AddGuideStep;
        _overlay.AutoSaved += path => _tray!.ShowInfo($"Copiada y guardada en {path}");
        _overlay.AutoSaveFailed += message => _tray!.ShowWarning($"Se copió, pero no se pudo guardar: {message}");
        _overlay.Closed += (_, _) =>
        {
            _overlay = null;
            ReleaseMemoryWhenIdle();
            OnMaybeIdle();
        };
        _overlay.Show();
    }

    // The screenshot of the whole desktop and its copies are tens of megabytes; once the overlay is gone they are only garbage.
    // Waiting for idle lets the window finish closing first, so nothing still points at them.
    private void ReleaseMemoryWhenIdle() =>
        Dispatcher.BeginInvoke(MemoryTrim.Release, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
}
