using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Recording;

/// <summary>A big number in the middle of the area that counts down to the start of a recording.</summary>
internal sealed class RecordingCountdown : Window
{
    private const int SizePixels = 160;

    private readonly TextBlock _number = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI"),
        FontWeight = FontWeights.SemiBold,
        FontSize = 80,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public RecordingCountdown(Rectangle region)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        // The Viewbox keeps the circle the same size on the screen whatever the scaling.
        Content = new Viewbox
        {
            Child = new Grid
            {
                Width = 120,
                Height = 120,
                Children =
                {
                    new Border { CornerRadius = new CornerRadius(60), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x21, 0x24)) },
                    _number,
                },
            },
        };

        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowNative.MakeClickThrough(hwnd);
        int side = Math.Min(SizePixels, Math.Min(region.Width, region.Height)); // never bigger than a small area
        WindowNative.Place(hwnd, new Rectangle(region.X + (region.Width - side) / 2, region.Y + (region.Height - side) / 2, side, side));
    }

    public void ShowNumber(int number) => _number.Text = number.ToString();
}
