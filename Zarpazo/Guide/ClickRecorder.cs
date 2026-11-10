using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Zarpazo.Capture;

namespace Zarpazo.Guide;

/// <summary>
/// While it runs, every left click adds a step: a picture of the window that was clicked, taken just before the click lands,
/// with a numbered ring on the spot. Clicks on the control bar itself are left out.
/// </summary>
internal sealed class ClickRecorder : IDisposable
{
    private readonly GuideDocument _guide;
    private readonly Func<Rectangle> _barBounds;
    private readonly MouseHook _hook;

    public ClickRecorder(GuideDocument guide, Func<Rectangle> barBounds)
    {
        _guide = guide;
        _barBounds = barBounds;
        _hook = new MouseHook();
        _hook.LeftButtonDown += OnClick;
    }

    /// <summary>A step was added.</summary>
    public event Action<GuideStep>? StepAdded;

    /// <summary>A click could not be turned into a step (the reason).</summary>
    public event Action<string>? Failed;

    private void OnClick(Point click)
    {
        if (_barBounds().Contains(click))
        {
            return;
        }

        // Taken here, inside the hook, so the screen is still as it was; the rest waits until the click has been delivered.
        Rectangle area = AreaFor(click);
        BitmapSource picture;
        try
        {
            picture = ScreenCapture.CaptureRegion(area);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            Failed?.Invoke($"No se pudo capturar la pantalla: {ex.Message}");
            return;
        }

        var at = new System.Windows.Point(click.X - area.X, click.Y - area.Y);
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => AddStep(picture, at));
    }

    private void AddStep(BitmapSource picture, System.Windows.Point at)
    {
        try
        {
            GuideStep step = _guide.Add(ClickMarker.Apply(picture, at, _guide.Steps.Count + 1));
            StepAdded?.Invoke(step);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke($"No se pudo guardar el paso: {ex.Message}");
        }
    }

    /// <summary>The window under the pointer; when there is none (the desktop), the monitor it is on.</summary>
    internal static Rectangle AreaFor(Point click, IReadOnlyList<Rectangle>? windows = null, Rectangle? screen = null)
    {
        Rectangle monitor = screen ?? Screen.FromPoint(click).Bounds;
        foreach (Rectangle window in windows ?? WindowFinder.GetVisibleWindowBounds()) // topmost first
        {
            if (window.Contains(click))
            {
                Rectangle visible = Rectangle.Intersect(window, SystemInformation.VirtualScreen);
                return visible.Width > 0 && visible.Height > 0 ? visible : monitor;
            }
        }
        return monitor;
    }

    public void Dispose()
    {
        _hook.LeftButtonDown -= OnClick;
        _hook.Dispose();
    }
}
