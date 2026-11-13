using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zarpazo.Output;

namespace Zarpazo.Recording;

/// <summary>
/// What is behind the recording in its frame: the background and the shadow, drawn once (they do not move) with WPF, the
/// same way Embellecer dresses up a capture. Needs a thread that can use WPF (the window's).
/// </summary>
internal static class FrameBackdrop
{
    private const double ShadowOpacity = 0.8; // at full strength
    private const double ShadowBlurPerHeight = 0.035;
    private const double ShadowDepthPerBlur = 0.3;
    private const double ShadowDown = 270;

    /// <summary>The backdrop as BGRA pixels, top row first, the scene's size. Callable from any thread: it draws on one of its own.</summary>
    public static byte[] Render(VideoFrameStyle style, FrameLayout layout)
    {
        var pixels = new byte[layout.Width * layout.Height * 4];
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Picture(style, layout, 1).CopyPixels(pixels, layout.Width * 4, 0);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA); // WPF draws only on such a thread
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw new InvalidOperationException("The frame's background could not be drawn.", failure);
        }
        return pixels;
    }

    /// <summary>The backdrop as a picture, smaller by <paramref name="scale"/> for the editor's preview.</summary>
    public static BitmapSource Picture(VideoFrameStyle style, FrameLayout layout, double scale)
    {
        int width = Math.Max(1, (int)Math.Round(layout.Width * scale));
        int height = Math.Max(1, (int)Math.Round(layout.Height * scale));
        var root = new Canvas
        {
            Width = width,
            Height = height,
            Background = BeautifyBackground.Find(style.Background).ToBrush(),
            ClipToBounds = true,
        };

        if (style.Shadow > 0)
        {
            // A dark card exactly under the recording: only its shadow shows, the recording covers the rest.
            double blur = Math.Max(4, layout.Height * ShadowBlurPerHeight * scale);
            var card = new Rectangle
            {
                Width = layout.Content.Width * scale,
                Height = layout.Content.Height * scale,
                RadiusX = layout.RadiusPixels * scale,
                RadiusY = layout.RadiusPixels * scale,
                Fill = Brushes.Black,
                Effect = new DropShadowEffect
                {
                    BlurRadius = blur,
                    ShadowDepth = blur * ShadowDepthPerBlur,
                    Direction = ShadowDown,
                    Opacity = ShadowOpacity * style.Shadow,
                    Color = Colors.Black,
                },
            };
            Canvas.SetLeft(card, layout.Content.X * scale);
            Canvas.SetTop(card, layout.Content.Y * scale);
            root.Children.Add(card);
        }

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(root);
        result.Freeze();
        return result;
    }
}
