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
    private const double ShadowDown = 270;

    /// <summary>The backdrop as BGRA pixels, top row first, the scene's size. Callable from any thread: it draws on one of its own.</summary>
    public static byte[] Render(VideoFrameStyle style, FrameLayout layout)
    {
        var pixels = new byte[layout.Width * layout.Height * 4];
        OnWpfThread(() => Picture(style, layout, 1).CopyPixels(pixels, layout.Width * 4, 0));
        return pixels;
    }

    /// <summary>
    /// The title bar as BGRA pixels, the card's width by the bar's height, top row first; null when the style has no bar. Square
    /// at the corners: the card's rounded edge is cut by whoever lays the bar down.
    /// </summary>
    public static byte[]? RenderBar(VideoFrameStyle style, FrameLayout layout)
    {
        if (layout.BarHeight <= 0 || style.Bar == WindowBar.None)
        {
            return null;
        }

        var pixels = new byte[layout.Card.Width * layout.BarHeight * 4];
        OnWpfThread(() =>
        {
            var bar = new Border();
            FillBar(bar, style, layout, 1, roundedTop: false);
            bar.Measure(new Size(bar.Width, bar.Height));
            bar.Arrange(new Rect(0, 0, bar.Width, bar.Height));
            bar.UpdateLayout();
            var result = new RenderTargetBitmap(layout.Card.Width, layout.BarHeight, 96, 96, PixelFormats.Pbgra32);
            result.Render(bar);
            result.CopyPixels(pixels, layout.Card.Width * 4, 0);
        });
        return pixels;
    }

    // WPF draws only on a thread of its own kind (STA), so the work is done on a new one and waited for.
    private static void OnWpfThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw new InvalidOperationException("The frame's background could not be drawn.", failure);
        }
    }

    private static readonly Color DarkBar = Color.FromRgb(0x2B, 0x2D, 0x31);
    private static readonly Color DarkBarLine = Color.FromRgb(0x1B, 0x1C, 0x1F);
    private static readonly Color DarkBarGlyph = Color.FromRgb(0xA8, 0xAD, 0xB4);
    private static readonly Color LightBar = Color.FromRgb(0xF1, 0xF3, 0xF4);
    private static readonly Color LightBarLine = Color.FromRgb(0xD5, 0xD8, 0xDC);
    private static readonly Color LightBarGlyph = Color.FromRgb(0x5F, 0x63, 0x68);

    /// <summary>
    /// Sets up <paramref name="bar"/> as the title bar of the card, for a scene drawn <paramref name="scale"/> times its size: its
    /// size, colour and buttons. The editor's preview keeps one in the scene; the saved video draws one into <see cref="RenderBar"/>.
    /// </summary>
    /// <param name="roundedTop">The card's top corners are round (the preview clips with them; the saved video cuts them itself).</param>
    public static void FillBar(Border bar, VideoFrameStyle style, FrameLayout layout, double scale, bool roundedTop)
    {
        double radius = roundedTop ? Math.Min(layout.RadiusPixels, Math.Min(layout.Card.Width, layout.Card.Height) / 2) * scale : 0;
        FillBar(bar, style.Bar, style.LightBar, layout.Card.Width * scale, layout.BarHeight * scale, radius);
    }

    /// <summary>Sets up <paramref name="bar"/> as a title bar of the given size; Embellecer uses it for captures too.</summary>
    public static void FillBar(Border bar, WindowBar kind, bool light, double width, double height, double radius)
    {
        Color glyph = light ? LightBarGlyph : DarkBarGlyph;

        bar.Width = width;
        bar.Height = height;
        bar.Background = new SolidColorBrush(light ? LightBar : DarkBar);
        bar.BorderBrush = new SolidColorBrush(light ? LightBarLine : DarkBarLine);
        bar.BorderThickness = new Thickness(0, 0, 0, Math.Max(1, height * 0.03));
        bar.CornerRadius = new CornerRadius(radius, radius, 0, 0);

        var canvas = new Canvas { Width = width, Height = height };
        double middle = height / 2;
        if (kind == WindowBar.Mac)
        {
            // Three round buttons: close, minimise, zoom.
            double size = height * 0.32, step = height * 0.56, first = height * 0.62;
            Color[] colours = [Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40)];
            for (int i = 0; i < 3; i++)
            {
                var dot = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(colours[i]) };
                Canvas.SetLeft(dot, first + i * step - size / 2);
                Canvas.SetTop(dot, middle - size / 2);
                canvas.Children.Add(dot);
            }
        }
        else if (kind == WindowBar.Windows)
        {
            // Minimise, maximise and close, the last at the right edge.
            double button = height * 1.3, mark = height * 0.26, line = Math.Max(1, height * 0.055);
            var brush = new SolidColorBrush(glyph);
            double closeX = width - button / 2, maximiseX = closeX - button, minimiseX = maximiseX - button;

            Add(canvas, new Line { X1 = minimiseX - mark / 2, X2 = minimiseX + mark / 2, Y1 = middle, Y2 = middle, Stroke = brush, StrokeThickness = line });
            var square = new Rectangle { Width = mark, Height = mark, Stroke = brush, StrokeThickness = line };
            Canvas.SetLeft(square, maximiseX - mark / 2);
            Canvas.SetTop(square, middle - mark / 2);
            canvas.Children.Add(square);
            Add(canvas, new Line { X1 = closeX - mark / 2, X2 = closeX + mark / 2, Y1 = middle - mark / 2, Y2 = middle + mark / 2, Stroke = brush, StrokeThickness = line });
            Add(canvas, new Line { X1 = closeX - mark / 2, X2 = closeX + mark / 2, Y1 = middle + mark / 2, Y2 = middle - mark / 2, Stroke = brush, StrokeThickness = line });
        }
        bar.Child = canvas;
    }

    private static void Add(Canvas canvas, Line line) => canvas.Children.Add(line);

    /// <summary>
    /// The shadow under the recording, for a scene drawn <paramref name="scale"/> times its size. The editor's preview puts it
    /// on a live card (the graphics card draws it, so a slider moves smoothly); the saved video draws it into the backdrop.
    /// </summary>
    public static DropShadowEffect Shadow(VideoFrameStyle style, FrameLayout layout, double scale)
    {
        return new DropShadowEffect
        {
            BlurRadius = layout.Height * style.ShadowBlur * scale,
            ShadowDepth = layout.Height * style.ShadowOffset * scale,
            Direction = ShadowDown,
            Opacity = ShadowOpacity * style.Shadow,
            Color = FrameBackground.TryHex(style.ShadowColor, out Color color) ? color : Colors.Black,
        };
    }

    /// <summary>The backdrop as a picture, smaller by <paramref name="scale"/>.</summary>
    public static BitmapSource Picture(VideoFrameStyle style, FrameLayout layout, double scale)
    {
        int width = Math.Max(1, (int)Math.Round(layout.Width * scale));
        int height = Math.Max(1, (int)Math.Round(layout.Height * scale));
        var root = new Canvas
        {
            Width = width,
            Height = height,
            Background = FrameBackground.ToBrush(style.Background),
            ClipToBounds = true,
        };

        if (style.Shadow > 0)
        {
            // A dark card exactly under the recording and its bar: only its shadow shows, they cover the rest.
            Int32Rect window = layout.Card;
            var card = new Rectangle
            {
                Width = window.Width * scale,
                Height = window.Height * scale,
                RadiusX = layout.RadiusPixels * scale,
                RadiusY = layout.RadiusPixels * scale,
                Fill = Brushes.Black,
                Effect = Shadow(style, layout, scale),
            };
            Canvas.SetLeft(card, window.X * scale);
            Canvas.SetTop(card, window.Y * scale);
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
