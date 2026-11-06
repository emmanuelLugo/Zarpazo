using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using Zarpazo.Capture;
using Zarpazo.Overlay;
using Zarpazo.Shell;
using Application = System.Windows.Application;

namespace Zarpazo;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\Zarpazo.SingleInstance";
    private const int MenuCloseDelayMs = 200;

    private Mutex? _instanceMutex;
    private TrayIcon? _tray;
    private PrintScreenHotKey? _hotKey;
    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _tray = new TrayIcon(CaptureFromMenu, Shutdown);
        _hotKey = new PrintScreenHotKey(ShowOverlay);
        if (!_hotKey.IsRegistered)
        {
            _tray.ShowWarning("Otra aplicación ya usa la tecla Impr Pant. Puedes capturar desde este icono.");
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

        _overlay = new OverlayWindow(image, bounds, windows);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
    }
}
