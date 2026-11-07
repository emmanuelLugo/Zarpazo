namespace Zarpazo.Recording;

/// <summary>
/// The text shown for a key press in a recording. Only shortcuts and special keys are shown, never the letters, digits or
/// punctuation that are being typed, so a password typed during a recording does not end up readable in the video.
/// </summary>
internal static class KeyLabels
{
    public const int Control = 0x11;
    public const int Alt = 0x12;
    public const int Shift = 0x10;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;

    /// <summary>The label to show for pressing <paramref name="virtualKey"/> with those modifiers held, or null if it should not be shown.</summary>
    public static string? Describe(int virtualKey, bool control, bool alt, bool shift, bool windows)
    {
        string? name = SpecialName(virtualKey);
        // Shift alone doesn't make a shortcut: it is how capital letters and symbols are typed.
        bool isShortcut = control || alt || windows;
        if (name == null && !isShortcut)
        {
            return null;
        }

        name ??= PlainName(virtualKey);
        if (name == null)
        {
            return null;
        }

        var parts = new List<string>();
        if (control) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Mayús");
        if (windows) parts.Add("Win");
        parts.Add(name);
        return string.Join("+", parts);
    }

    /// <summary>True for the keys that are only modifiers: pressing them is not, by itself, something to show.</summary>
    public static bool IsModifier(int virtualKey) =>
        virtualKey is Shift or Control or Alt or LeftWindows or RightWindows or (>= 0xA0 and <= 0xA5);

    /// <summary>True for the mouse buttons, which are reported through the same keys.</summary>
    public static bool IsMouseButton(int virtualKey) => virtualKey is >= 0x01 and <= 0x06;

    private static string? SpecialName(int virtualKey) => virtualKey switch
    {
        0x0D => "Enter",
        0x1B => "Esc",
        0x09 => "Tab",
        0x08 => "Retroceso",
        0x2E => "Supr",
        0x2D => "Insert",
        0x24 => "Inicio",
        0x23 => "Fin",
        0x21 => "Re Pág",
        0x22 => "Av Pág",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2C => "Impr Pant",
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        _ => null,
    };

    // Letters, digits and the space bar: only ever reached together with Ctrl, Alt or Win.
    private static string? PlainName(int virtualKey) => virtualKey switch
    {
        0x20 => "Espacio",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => ((char)('0' + virtualKey - 0x60)).ToString(), // numeric keypad
        0xBB => "+",
        0xBD => "-",
        0xBC => ",",
        0xBE => ".",
        _ => null,
    };
}
