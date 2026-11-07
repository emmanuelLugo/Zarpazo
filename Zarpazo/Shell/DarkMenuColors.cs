using System.Drawing;
using System.Windows.Forms;

namespace Zarpazo.Shell;

/// <summary>The colours of the tray menu in dark mode; the light menu keeps the ones Windows gives it.</summary>
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    internal static readonly Color Background = Color.FromArgb(0x26, 0x27, 0x2B);
    internal static readonly Color Text = Color.FromArgb(0xE8, 0xEA, 0xED);
    private static readonly Color Highlight = Color.FromArgb(0x3B, 0x3E, 0x44);
    private static readonly Color Line = Color.FromArgb(0x3A, 0x3D, 0x43);

    public DarkMenuColors() => UseSystemColors = false;

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
