using System.Windows.Input;

namespace Zarpazo.Shell;

/// <summary>A global shortcut: Win32 modifier flags plus a virtual-key code.</summary>
internal readonly record struct HotKeyBinding(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 1;
    public const uint Control = 2;
    public const uint Shift = 4;
    public const uint Windows = 8;

    public const uint VkSnapshot = 0x2C;
    private const uint VkPageUp = 0x21; // Page Up, Page Down, End, Home are 0x21 to 0x24
    private const uint VkHome = 0x24;
    private const uint VkF1 = 0x70;
    private const uint VkF24 = 0x87;

    private const uint VkR = 0x52;
    private const uint VkC = 0x43;

    public static HotKeyBinding Default => new(0, VkSnapshot);

    /// <summary>Ctrl+Alt+R: starts or stops a recording of the last area.</summary>
    public static HotKeyBinding DefaultRecord => new(Control | Alt, VkR);

    /// <summary>Ctrl+Alt+C: copies the group of captures and empties it.</summary>
    public static HotKeyBinding DefaultGroup => new(Control | Alt, VkC);

    /// <summary>
    /// A shortcut that would break typing is refused: a letter alone, or with only Shift, would be taken from every
    /// program. Only Print Screen and the function keys may go without Ctrl, Alt or Win, and Shift alone is enough
    /// with Page Up, Page Down, End or Home (the person chose to give up selecting by page in other programs).
    /// </summary>
    public bool IsAcceptable =>
        VirtualKey != 0
        && ((Modifiers & (Alt | Control | Windows)) != 0
            || VirtualKey == VkSnapshot
            || VirtualKey is >= VkF1 and <= VkF24
            || ((Modifiers & Shift) != 0 && VirtualKey is >= VkPageUp and <= VkHome));

    public static HotKeyBinding From(Key key, ModifierKeys modifiers)
    {
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= Windows;
        return new HotKeyBinding(flags, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    /// <summary>The text shown to the person, such as "Ctrl+Mayús+S".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if ((Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Mayús");
        if ((Modifiers & Windows) != 0) parts.Add("Win");

        // shortcut: keys without a special case show WPF's English name (such as OemTilde), when someone asks, map them.
        Key key = KeyInterop.KeyFromVirtualKey((int)VirtualKey);
        parts.Add(key switch
        {
            Key.Snapshot => "Impr Pant",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            Key.Return => "Enter",
            Key.Space => "Espacio",
            Key.PageUp => "RePág",
            Key.PageDown => "AvPág",
            Key.Home => "Inicio",
            Key.End => "Fin",
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }
}
