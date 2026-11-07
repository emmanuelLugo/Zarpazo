using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using Zarpazo.Capture;
using Zarpazo.History;
using Zarpazo.Overlay;
using Zarpazo.Recording;
using Zarpazo.Settings;
using Zarpazo.Shell;
using Application = System.Windows.Application;

namespace Zarpazo;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\Zarpazo.SingleInstance";
    private const int MenuCloseDelayMs = 200;

    private Mutex? _instanceMutex;
    private TrayIcon? _tray;
    private GlobalHotKey? _hotKey;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private RecordingSession? _recording;
    private readonly CaptureHistory _history = new(CaptureHistory.DefaultFolder);
    private SettingsStore _settings = null!; // created in OnStartup, before anything can use it

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
        _tray = new TrayIcon(CaptureFromMenu, ShowHistory, ShowSettings, Shutdown);
        _hotKey = new GlobalHotKey(ShowOverlay);
        if (!_hotKey.TryRegister(_settings.Current.HotKey))
        {
            _tray.ShowWarning($"Otra aplicación ya usa {_settings.Current.HotKey.Describe()}. Puedes cambiar el atajo en Configuración o capturar desde este icono.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
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
            };
            _overlay.Show();
        }
        catch (Exception ex)
        {
            _historyWindow?.Show();
            _tray!.ShowWarning($"No se pudo abrir la captura: {ex.Message}");
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        // While the shortcut is registered Windows keeps the key from the window, so it couldn't be recorded.
        _hotKey!.Unregister();
        _settingsWindow = new SettingsWindow(_settings, new StartupRegistration(), _hotKey.TryRegister);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (!_hotKey.TryRegister(_settings.Current.HotKey))
            {
                _tray!.ShowWarning($"No se pudo activar {_settings.Current.HotKey.Describe()}: otra aplicación lo usa.");
            }
        };
        _settingsWindow.Show();
    }

    // Starts recording once the overlay has gone, so it can't end up in the first pictures.
    private async void BeginRecording(Rectangle region, RecordingOptions options)
    {
        await Task.Delay(MenuCloseDelayMs);
        _recording?.Close(); // a finished one still on screen makes way for the new one
        var session = new RecordingSession(region, options, _settings, () => _recording = null);
        _recording = session;
        try
        {
            session.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            session.Close();
            _tray!.ShowWarning($"No se pudo empezar a grabar: {ex.Message}");
        }
    }

    private void ShowOverlay()
    {
        // The same shortcut that starts a capture stops a recording: it is the one key that is always within reach.
        if (_recording is { IsRecording: true })
        {
            _recording.Stop();
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

        _overlay = new OverlayWindow(image, bounds, windows, _settings, _history);
        _overlay.RecordRequested += BeginRecording;
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
    }
}
