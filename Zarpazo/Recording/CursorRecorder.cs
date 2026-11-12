using System.Drawing;

namespace Zarpazo.Recording;

/// <summary>Writes down where the pointer goes and where it clicks while a video records, for the zoom of the editor.</summary>
internal sealed class CursorRecorder
{
    private static readonly (int Key, ClickButton Button)[] Buttons =
    [
        (0x01, ClickButton.Left),
        (0x02, ClickButton.Right),
        (0x04, ClickButton.Middle),
    ];

    private readonly IInputState _input;
    private readonly Rectangle _region;
    private readonly int _width;
    private readonly int _height;
    private readonly List<CursorSample> _moves = [];
    private readonly List<CursorClick> _clicks = [];
    private readonly int?[] _heldClick = new int?[Buttons.Length]; // the click each button is holding down, as an index into _clicks

    /// <param name="region">The part of the screen being recorded, in physical pixels.</param>
    /// <param name="width">The size of the video, which can be smaller than the area.</param>
    public CursorRecorder(IInputState input, Rectangle region, int width, int height)
    {
        _input = input;
        _region = region;
        _width = width;
        _height = height;
    }

    /// <summary>Looks at the mouse once; call it for every picture, with the picture's time.</summary>
    public void Update(long timeMs)
    {
        Point screen = _input.CursorPosition;
        int x = (int)Math.Round((screen.X - _region.X) * (double)_width / _region.Width);
        int y = (int)Math.Round((screen.Y - _region.Y) * (double)_height / _region.Height);
        if (_moves.Count == 0 || _moves[^1].X != x || _moves[^1].Y != y)
        {
            _moves.Add(new CursorSample(timeMs, x, y));
        }

        for (int i = 0; i < Buttons.Length; i++)
        {
            bool down = _input.IsDown(Buttons[i].Key);
            if (down && _heldClick[i] == null)
            {
                _heldClick[i] = _clicks.Count;
                _clicks.Add(new CursorClick(timeMs, timeMs, x, y, Buttons[i].Button));
            }
            else if (!down && _heldClick[i] is int held)
            {
                _clicks[held] = _clicks[held] with { EndMs = timeMs };
                _heldClick[i] = null;
            }
            else if (!down && _input.WasPressedSinceLastCheck(Buttons[i].Key))
            {
                _clicks.Add(new CursorClick(timeMs, timeMs, x, y, Buttons[i].Button)); // down and up between two pictures
            }
        }
    }

    /// <summary>What was recorded so far. A button still held counts as released at <paramref name="endMs"/>.</summary>
    public CursorTrack ToTrack(long endMs)
    {
        var clicks = new List<CursorClick>(_clicks);
        foreach (int? held in _heldClick)
        {
            if (held is int index)
            {
                clicks[index] = clicks[index] with { EndMs = Math.Max(clicks[index].TimeMs, endMs) };
            }
        }
        return new CursorTrack(_width, _height, _moves.ToList(), clicks);
    }
}
