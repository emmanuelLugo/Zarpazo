using System.Drawing;

namespace Zarpazo.Recording;

/// <summary>
/// The keyboard and mouse read once per picture and shared by everyone who looks during that picture. Windows clears the
/// "pressed since the last check" bit when it is read, so two readers asking separately would each see only some of the quick
/// clicks; here the first answer is kept until <see cref="Refresh"/>.
/// </summary>
internal sealed class FrameInputState(IInputState input) : IInputState
{
    private readonly Dictionary<int, bool> _down = [];
    private readonly Dictionary<int, bool> _pressed = [];
    private Point? _cursor;

    /// <summary>Forgets what was read, so the next questions look at the real keyboard and mouse again. Call it once per picture.</summary>
    public void Refresh()
    {
        _down.Clear();
        _pressed.Clear();
        _cursor = null;
    }

    public bool IsDown(int virtualKey)
    {
        if (!_down.TryGetValue(virtualKey, out bool down))
        {
            down = input.IsDown(virtualKey);
            _down[virtualKey] = down;
        }
        return down;
    }

    public bool WasPressedSinceLastCheck(int virtualKey)
    {
        if (!_pressed.TryGetValue(virtualKey, out bool pressed))
        {
            pressed = input.WasPressedSinceLastCheck(virtualKey);
            _pressed[virtualKey] = pressed;
        }
        return pressed;
    }

    public Point CursorPosition => _cursor ??= input.CursorPosition;
}
