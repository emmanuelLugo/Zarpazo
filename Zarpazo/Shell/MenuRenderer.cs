using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zarpazo.Shell;

/// <summary>
/// Draws the tray menu in Zarpazo's look: a rounded highlight with a thin red bar on the item under the pointer. The stock
/// renderer takes that colour from Windows whatever the colour table says, which left a light-blue bar with unreadable text
/// in dark mode.
/// </summary>
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    private readonly MenuColors _colors;

    public MenuRenderer(MenuColors colors) : base(colors)
    {
        _colors = colors;
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var row = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using (GraphicsPath path = Rounded(row, 6))
        using (var fill = new SolidBrush(_colors.Highlight))
        {
            g.FillPath(fill, path);
        }
        using GraphicsPath bar = Rounded(new Rectangle(row.X, row.Y + 5, 3, row.Height - 10), 1);
        using var red = new SolidBrush(_colors.Accent);
        g.FillPath(red, bar);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _colors.Text : _colors.Disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Enabled: false } ? _colors.Disabled : _colors.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // The tick of "Modo grupo" in the accent colour instead of the stock black one.
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle box = e.ImageRectangle;
        float x = box.X + box.Width / 2f, y = box.Y + box.Height / 2f;
        using var pen = new Pen(_colors.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(x - 4, y), new PointF(x - 1, y + 3), new PointF(x + 4, y - 3)]);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
