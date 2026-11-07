namespace Zarpazo.Recording;

/// <summary>Watches the keyboard and keeps the last few shortcuts pressed, each for a short while, so they can be drawn on the recording.</summary>
internal sealed class KeyTracker(IInputState input)
{
    private const int ShownMilliseconds = 1600;
    private const int MaxShown = 3;
    private const int FirstKey = 0x08;
    private const int LastKey = 0xFE;

    private readonly bool[] _wasDown = new bool[LastKey + 1];
    private readonly List<(string Label, long Time)> _shown = [];

    /// <summary>Looks at the keyboard once; call it for every picture taken.</summary>
    public void Update(long timeMs)
    {
        bool control = input.IsDown(KeyLabels.Control);
        bool alt = input.IsDown(KeyLabels.Alt);
        bool shift = input.IsDown(KeyLabels.Shift);
        bool windows = input.IsDown(KeyLabels.LeftWindows) || input.IsDown(KeyLabels.RightWindows);

        for (int key = FirstKey; key <= LastKey; key++)
        {
            bool down = input.IsDown(key);
            bool justPressed = down && !_wasDown[key];
            _wasDown[key] = down;

            if (!justPressed || KeyLabels.IsModifier(key) || KeyLabels.IsMouseButton(key))
            {
                continue;
            }

            if (KeyLabels.Describe(key, control, alt, shift, windows) is { } label)
            {
                _shown.Add((label, timeMs));
            }
        }

        _shown.RemoveAll(entry => timeMs - entry.Time > ShownMilliseconds);
        while (_shown.Count > MaxShown)
        {
            _shown.RemoveAt(0);
        }
    }

    /// <summary>What to draw now: the recent shortcuts, oldest first; empty when there is nothing to show.</summary>
    public IReadOnlyList<string> Current => _shown.Select(entry => entry.Label).ToList();
}
