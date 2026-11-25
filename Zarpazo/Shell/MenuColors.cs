using System.Drawing;
using System.Windows.Forms;

namespace Zarpazo.Shell;

/// <summary>The colours of the tray menu, the same ones the windows take from Dark.xaml and Light.xaml.</summary>
internal sealed class MenuColors : ProfessionalColorTable
{
    internal Color Background { get; }
    internal Color Text { get; }
    internal Color Disabled { get; }
    internal Color Highlight { get; }
    internal Color Accent { get; }
    private Color Line { get; }

    public MenuColors(bool dark)
    {
        UseSystemColors = false;
        Background = dark ? Color.FromArgb(0x26, 0x27, 0x2B) : Color.White;
        Text = dark ? Color.FromArgb(0xE8, 0xEA, 0xED) : Color.FromArgb(0x1F, 0x23, 0x28);
        Disabled = dark ? Color.FromArgb(0x6A, 0x6F, 0x78) : Color.FromArgb(0xA0, 0xA7, 0xB2);
        Highlight = dark ? Color.FromArgb(0x3B, 0x3E, 0x44) : Color.FromArgb(0xEB, 0xEE, 0xF2);
        Accent = dark ? Color.FromArgb(0xEF, 0x53, 0x50) : Color.FromArgb(0xE5, 0x39, 0x35);
        Line = dark ? Color.FromArgb(0x3A, 0x3D, 0x43) : Color.FromArgb(0xD8, 0xDC, 0xE2);
    }

    public override Color ToolStripDropDownBackground => Background;
    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;
    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Highlight;
    public override Color MenuItemSelected => Highlight;
    public override Color MenuItemSelectedGradientBegin => Highlight;
    public override Color MenuItemSelectedGradientEnd => Highlight;
    public override Color MenuItemPressedGradientBegin => Highlight;
    public override Color MenuItemPressedGradientMiddle => Highlight;
    public override Color MenuItemPressedGradientEnd => Highlight;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
    public override Color CheckBackground => Highlight;
}
