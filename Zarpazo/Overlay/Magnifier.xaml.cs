using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zarpazo.Overlay;

/// <summary>Shows the screen pixels around the cursor, enlarged, and the color of the one under it.</summary>
public sealed partial class Magnifier : UserControl
{
    private const int Radius = 5;
    private const int Side = 2 * Radius + 1; // pixels shown across
    private const int BytesPerPixel = 4;      // the screenshot is Bgr32, as the pixelate tool also assumes
    private const string DefaultHint = "X: HEX/RGB · clic: copiar";

    private bool _asRgb;
    private string? _caption;

    public Magnifier()
    {
        InitializeComponent();
        Hint.Text = DefaultHint;
    }

    public Color Current { get; private set; }

    /// <summary>The color as it is copied: #RRGGBB or rgb(r, g, b).</summary>
    public string Text => _asRgb ? $"rgb({Current.R}, {Current.G}, {Current.B})" : $"#{Current.R:X2}{Current.G:X2}{Current.B:X2}";

    /// <summary>Centers the view on an image pixel. Pixels past the screen edge show as dark gray.</summary>
    /// <param name="caption">Shown instead of the color value (the position, while choosing an area); the color then goes in the hint line.</param>
    public void Show(BitmapSource image, int x, int y, string? caption = null)
    {
        x = Math.Clamp(x, 0, image.PixelWidth - 1);
        y = Math.Clamp(y, 0, image.PixelHeight - 1);

        int left = Math.Max(x - Radius, 0);
        int top = Math.Max(y - Radius, 0);
        int width = Math.Min(x + Radius + 1, image.PixelWidth) - left;
        int height = Math.Min(y + Radius + 1, image.PixelHeight) - top;

        var region = new byte[width * height * BytesPerPixel];
        image.CopyPixels(new Int32Rect(left, top, width, height), region, width * BytesPerPixel, 0);

        var view = new byte[Side * Side * BytesPerPixel];
        Array.Fill(view, (byte)0x26);
        for (int row = 0; row < height; row++)
        {
            int target = ((top - (y - Radius) + row) * Side + (left - (x - Radius))) * BytesPerPixel;
            Buffer.BlockCopy(region, row * width * BytesPerPixel, view, target, width * BytesPerPixel);
        }

        BitmapSource zoom = BitmapSource.Create(Side, Side, 96, 96, PixelFormats.Bgr32, null, view, Side * BytesPerPixel);
        zoom.Freeze();
        Zoom.Source = zoom;

        int center = ((y - top) * width + (x - left)) * BytesPerPixel;
        Current = Color.FromRgb(region[center + 2], region[center + 1], region[center]);
        _caption = caption;
        Hint.Text = caption == null ? DefaultHint : Text;
        Refresh();
    }

    public void ToggleFormat()
    {
        _asRgb = !_asRgb;
        Refresh();
    }

    /// <summary>Replaces the key hint until the cursor moves again.</summary>
    public void ShowNotice(string message) => Hint.Text = message;

    private void Refresh()
    {
        Swatch.Fill = new SolidColorBrush(Current);
        Value.Text = _caption ?? Text;
    }
}
