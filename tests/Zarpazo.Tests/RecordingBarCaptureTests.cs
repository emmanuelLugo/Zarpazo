using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Zarpazo.Recording;
using Rectangle = System.Drawing.Rectangle;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.SolidColorBrush;

namespace Zarpazo.Tests;

/// <summary>
/// The bar sits inside the recorded area when the whole screen is recorded, and Windows is asked to leave it out of every
/// copy of the screen. The bar is a see-through window (for its round corners), so this checks on the real screen that it
/// stays out, with both ways the recording copies the screen.
/// </summary>
[Collection("Recording")]
public sealed class RecordingBarCaptureTests
{
    private static readonly Rectangle Area = new(120, 120, 640, 240);

    private static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    // How many pixels of the picture are not the magenta of the window behind the bar.
    private static int NotMagenta(byte[] bgra)
    {
        int count = 0;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bool magenta = bgra[i] > 200 && bgra[i + 1] < 60 && bgra[i + 2] > 200;
            count += magenta ? 0 : 1;
        }
        return count;
    }

    // The picture as a PNG when ZARPAZO_SCREENSHOTS names a folder, to see what was copied.
    private static void Save(byte[] bgra, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        var picture = System.Windows.Media.Imaging.BitmapSource.Create(Area.Width, Area.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, bgra, Area.Width * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture));
        System.IO.Directory.CreateDirectory(folder);
        using System.IO.FileStream stream = System.IO.File.Create(System.IO.Path.Combine(folder, name));
        encoder.Save(stream);
    }

    [NeedsGpuFact]
    public void The_bar_over_the_recorded_area_is_not_in_the_pictures()
    {
        Sta.Run(() =>
        {
            // A plain magenta window fills the area, so anything else seen in it can only be the bar.
            var backdrop = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = new WpfBrush(WpfColor.FromRgb(255, 0, 255)),
            };
            var bar = new RecordingBar();
            backdrop.Show();
            WindowNative.Place(new WindowInteropHelper(backdrop).Handle, Area);
            try
            {
                bar.ShowRecording(TimeSpan.FromSeconds(9), TimeSpan.FromMinutes(30), []);
                bar.PlaceNear(new Rectangle(Area.X, Area.Y, Area.Width, 60)); // under this strip: inside the magenta
                bar.Show();
                Pump(600);
                IntPtr handle = new WindowInteropHelper(bar).Handle;
                Assert.True(GetWindowDisplayAffinity(handle, out uint affinity) && affinity != 0, "Windows was asked to leave the bar out");

                var screen = new byte[Area.Width * Area.Height * 4];
                using (var grabber = new RegionGrabber(Area, Area.Width, Area.Height, drawPointer: false))
                {
                    grabber.Grab(screen);
                    grabber.Grab(screen); // the card's copy can be a picture late at first
                    Save(screen, "bar-capture-card.png");
                    Assert.True(NotMagenta(screen) < Area.Width * Area.Height / 100,
                        $"{NotMagenta(screen)} pixels that are not the backdrop ({(grabber.UsesGraphicsCard ? "graphics card" : "CopyFromScreen")})");
                }

                using var bitmap = new Bitmap(Area.Width, Area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(Area.Location, System.Drawing.Point.Empty, Area.Size); // as RegionGrabber does on two screens
                }
                System.Drawing.Imaging.BitmapData data = bitmap.LockBits(new Rectangle(System.Drawing.Point.Empty, Area.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var copied = new byte[Area.Width * Area.Height * 4];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, copied, 0, copied.Length);
                bitmap.UnlockBits(data);
                Assert.True(NotMagenta(copied) < Area.Width * Area.Height / 100, $"{NotMagenta(copied)} pixels that are not the backdrop (CopyFromScreen)");

                // And it does show on the screen: let into the copies for a moment, it is there, see-through round the pill.
                SetWindowDisplayAffinity(handle, 0);
                Pump(300);
                using (var grabber = new RegionGrabber(Area, Area.Width, Area.Height, drawPointer: false))
                {
                    grabber.Grab(screen);
                    grabber.Grab(screen);
                }
                Save(screen, "bar-capture-visible.png");
                Assert.True(NotMagenta(screen) > 5000, "the bar shows on the screen");
                Assert.True(NotMagenta(screen) < Area.Width * Area.Height / 4, "only the pill shows, the rest of its window is see-through");
            }
            finally
            {
                bar.Close();
                backdrop.Close();
            }
        });
    }
}
