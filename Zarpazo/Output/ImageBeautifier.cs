using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Zarpazo.Output;

internal static class ImageBeautifier
{
    private const double ShadowOpacity = 0.45;
    private const double ShadowBlurPerPadding = 0.45; // the room around the picture is what the shadow has to fit in
    private const double MinShadowBlur = 6;
    private const double ShadowDepthPerBlur = 0.35;
    private const double ShadowDown = 270; // WPF angles run counterclockwise from the right

    /// <summary>The capture on its background. A <paramref name="scale"/> below 1 gives a proportionally smaller picture, for previews.</summary>
    public static BitmapSource Apply(BitmapSource image, BeautifyStyle style, double scale = 1)
    {
        int padding = (int)Math.Round(style.Padding * scale);
        int width = Math.Max(1, (int)Math.Round(image.PixelWidth * scale));
        int height = Math.Max(1, (int)Math.Round(image.PixelHeight * scale));
        int totalWidth = width + 2 * padding;
        int totalHeight = height + 2 * padding;
        double radius = style.Radius * scale;

        // A rectangle filled with the picture gives the rounded corners; its shadow follows that shape.
        var picture = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Fill = new ImageBrush(image) { Stretch = Stretch.Fill },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Sharp pixels at full size; anything smaller needs smoothing or it turns to noise.
        RenderOptions.SetBitmapScalingMode(picture, scale == 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
        if (style.Shadow && padding > 0)
        {
            double blur = Math.Max(MinShadowBlur * scale, padding * ShadowBlurPerPadding);
            picture.Effect = new DropShadowEffect
            {
                BlurRadius = blur,
                ShadowDepth = blur * ShadowDepthPerBlur,
                Direction = ShadowDown,
                Opacity = ShadowOpacity,
                Color = Colors.Black,
            };
        }

        var root = new Grid
        {
            Width = totalWidth,
            Height = totalHeight,
            Background = BeautifyBackground.Find(style.Background).ToBrush(),
            UseLayoutRounding = true,
            ClipToBounds = true, // the shadow must not spill past the background
        };
        root.Children.Add(picture);
        root.Measure(new Size(totalWidth, totalHeight));
        root.Arrange(new Rect(0, 0, totalWidth, totalHeight));
        root.UpdateLayout();

        var result = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(root);
        result.Freeze();
        return result;
    }
}
