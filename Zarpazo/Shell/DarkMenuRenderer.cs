using System.Drawing;
using System.Windows.Forms;

namespace Zarpazo.Shell;

/// <summary>
/// Draws the tray menu in dark colours. The stock renderer takes the colour of the item under the pointer from Windows
/// whatever the colour table says, which left a light-blue bar with unreadable text.
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color DisabledText = Color.FromArgb(0x6A, 0x6F, 0x78);

    public DarkMenuRenderer() : base(new DarkMenuColors()) => RoundedEdges = false;

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var brush = new SolidBrush(DarkMenuColors.Highlight);
            e.Graphics.FillRectangle(brush, new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? DarkMenuColors.Text : DisabledText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Enabled: false } ? DisabledText : DarkMenuColors.Text;
        base.OnRenderArrow(e);
    }
}
