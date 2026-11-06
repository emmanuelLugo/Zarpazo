using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using Zarpazo.Capture;
using Zarpazo.Overlay;
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
        _tray = new TrayIcon(CaptureFromMenu, ShowSettings, Shutdown);
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

    private void ShowOverlay()
    {
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

        _overlay = new OverlayWindow(image, bounds, windows, _settings);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
    }
}
