using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Zarpazo.Recording;
using Rectangle = System.Drawing.Rectangle;

namespace Zarpazo.Capture;

/// <summary>
/// A small strip at the top of the screen that says a scroll capture is running and how tall the picture is so far, with a button that
/// ends it (an endless feed otherwise runs to the longest picture allowed).
/// </summary>
internal sealed class ScrollStatusWindow : Window
{
    private const int WidthPixels = 420;
    private const int HeightPixels = 44;

    private readonly TextBlock _text = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Null when Windows would not keep the strip out of the pictures: showing it then would put it in the capture.</summary>
    /// <param name="stop">Called when the button is clicked.</param>
    public static ScrollStatusWindow? TryShow(Rectangle region, Action stop)
    {
        var window = new ScrollStatusWindow(stop);
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        WindowNative.MakeNoActivate(hwnd);
        WindowNative.KeepPixelSize(hwnd);
        if (!WindowNative.ExcludeFromScreenCapture(hwnd))
        {
            window.Close();
            return null;
        }

        Rectangle screen = System.Windows.Forms.Screen.FromRectangle(region).Bounds;
        WindowNative.Place(hwnd, new Rectangle(screen.X + (screen.Width - WidthPixels) / 2, screen.Y + 16, WidthPixels, HeightPixels));
        window.Show();
        return window;
    }

    private ScrollStatusWindow(Action stop)
    {
        var finish = new Button
        {
            Content = "Terminar",
            FontFamily = _text.FontFamily,
            FontSize = 13,
            Padding = new Thickness(12, 3, 12, 3),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        finish.Click += (_, _) =>
        {
            finish.IsEnabled = false;
            _text.Text = "Terminando…";
            stop();
        };
        var grid = new Grid { Width = WidthPixels, Height = HeightPixels };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(finish, 1);
        grid.Children.Add(_text);
        grid.Children.Add(finish);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24));
        Content = new Viewbox { Child = grid };
        Update(0);
    }

    /// <param name="rows">Height of the picture so far, in pixels.</param>
    public void Update(int rows) => _text.Text = rows == 0 ? "Capturando con scroll… (Esc termina)" : $"Capturando… {rows} px (Esc termina)";
}
