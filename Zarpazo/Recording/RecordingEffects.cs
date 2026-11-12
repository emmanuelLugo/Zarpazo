using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Zarpazo.Recording;

/// <summary>What is drawn on top of a recording to make it easier to follow: a halo around the pointer, a ring where the mouse is clicked, and the shortcuts pressed.</summary>
internal sealed class RecordingEffects
{
    private const int VkLeft = 0x01;
    private const int VkRight = 0x02;
    private const int VkMiddle = 0x04;
    private const int HaloRadius = 22;
    private const int HeldRadius = 18;
    private const int RippleMilliseconds = 450;
    private const int RippleStartRadius = 14;
    private const int RippleGrowth = 26;

    private static readonly (int Key, Color Color)[] Buttons =
    [
        (VkLeft, Color.FromArgb(0xFF, 0xC1, 0x07)),   // amber
        (VkRight, Color.FromArgb(0x42, 0xA5, 0xF5)),  // blue
        (VkMiddle, Color.FromArgb(0x66, 0xBB, 0x6A)), // green
    ];

    private readonly IInputState _input;
    private readonly bool _highlight;
    private readonly KeyTracker? _keys;
    private readonly bool[] _held = new bool[Buttons.Length];
    private readonly List<(Point Center, long Start, Color Color)> _ripples = [];
    private Point _cursor;

    public RecordingEffects(IInputState input, bool highlightCursor, bool showKeys)
    {
        _input = input;
        _highlight = highlightCursor;
        _keys = showKeys ? new KeyTracker(input) : null;
    }

    /// <summary>The effects the options ask for; null when they ask for none, so nothing is done for nothing.</summary>
    public static RecordingEffects? FromOptions(RecordingOptions options) => FromOptions(options, new WindowsInputState());

    /// <param name="input">Where the keyboard and mouse are read, when someone else reads them too.</param>
    public static RecordingEffects? FromOptions(RecordingOptions options, IInputState input) =>
        options.HighlightCursor || options.ShowKeys ? new RecordingEffects(input, options.HighlightCursor, options.ShowKeys) : null;

    /// <summary>Reads the mouse and the keyboard. Call it once per picture, before drawing.</summary>
    public void Update(long timeMs)
    {
        _cursor = _input.CursorPosition;
        for (int i = 0; i < Buttons.Length; i++)
        {
            bool down = _input.IsDown(Buttons[i].Key);
            // A click that is over before the next picture is taken still leaves its ring.
            bool clicked = (down && !_held[i]) || (!down && _input.WasPressedSinceLastCheck(Buttons[i].Key));
            if (clicked && _highlight)
            {
                _ripples.Add((_cursor, timeMs, Buttons[i].Color));
            }
            _held[i] = down;
        }
        _ripples.RemoveAll(ripple => timeMs - ripple.Start > RippleMilliseconds);
        _keys?.Update(timeMs);
    }

    /// <summary>The halo and the click rings, drawn before the pointer so the pointer stays on top.</summary>
    /// <param name="origin">Where the picture starts on the screen.</param>
    public void DrawUnderPointer(Graphics graphics, Point origin, long timeMs)
    {
        if (!_highlight)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float x = _cursor.X - origin.X;
        float y = _cursor.Y - origin.Y;

        using (var halo = new SolidBrush(Color.FromArgb(0x50, 0xFF, 0xEB, 0x3B)))
        {
            graphics.FillEllipse(halo, x - HaloRadius, y - HaloRadius, HaloRadius * 2, HaloRadius * 2);
        }

        // While a button is held its ring stays.
        for (int i = 0; i < Buttons.Length; i++)
        {
            if (_held[i])
            {
                using var fill = new SolidBrush(Color.FromArgb(0x60, Buttons[i].Color));
                using var edge = new Pen(Color.FromArgb(0xE0, Buttons[i].Color), 3);
                graphics.FillEllipse(fill, x - HeldRadius, y - HeldRadius, HeldRadius * 2, HeldRadius * 2);
                graphics.DrawEllipse(edge, x - HeldRadius, y - HeldRadius, HeldRadius * 2, HeldRadius * 2);
            }
        }

        // After the click, a ring that grows and fades.
        foreach ((Point center, long start, Color color) in _ripples)
        {
            double progress = Math.Clamp((timeMs - start) / (double)RippleMilliseconds, 0, 1);
            float radius = (float)(RippleStartRadius + RippleGrowth * progress);
            using var pen = new Pen(Color.FromArgb((int)(220 * (1 - progress)), color), 3);
            graphics.DrawEllipse(pen, center.X - origin.X - radius, center.Y - origin.Y - radius, radius * 2, radius * 2);
        }
    }

    /// <summary>The shortcuts pressed lately, in a dark pill at the bottom of the picture.</summary>
    public void DrawKeys(Graphics graphics, Size size)
    {
        IReadOnlyList<string> labels = _keys?.Current ?? [];
        if (labels.Count == 0)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float fontSize = Math.Clamp(size.Height / 16f, 14f, 28f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        string text = string.Join("   ", labels);
        SizeF measured = graphics.MeasureString(text, font);

        float padding = fontSize * 0.6f;
        var pill = new RectangleF(
            (size.Width - measured.Width) / 2 - padding,
            size.Height - measured.Height - padding * 2 - fontSize,
            measured.Width + padding * 2,
            measured.Height + padding);
        using (GraphicsPath path = RoundedRectangle(pill, pill.Height / 2))
        using (var background = new SolidBrush(Color.FromArgb(0xD8, 0x20, 0x21, 0x24)))
        {
            graphics.FillPath(background, path);
        }
        graphics.DrawString(text, font, Brushes.White, pill.X + padding, pill.Y + padding / 2);
    }

    private static GraphicsPath RoundedRectangle(RectangleF area, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(area.X, area.Y, d, d, 180, 90);
        path.AddArc(area.Right - d, area.Y, d, d, 270, 90);
        path.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
        path.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
