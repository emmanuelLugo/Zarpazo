# Draws the Zarpazo icon and writes Zarpazo\Assets\Zarpazo.ico (sizes 16 to 256).
# Run from the repo root with PowerShell 7:  pwsh tools/make-icon.ps1
# Optional: -PreviewPng <path> also saves a sheet with every size, enlarged, for checking the result by eye.
param(
    [string]$OutIco = "$PSScriptRoot\..\Zarpazo\Assets\Zarpazo.ico",
    [string]$PreviewPng
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Newer PowerShell builds keep part of System.Drawing in assemblies the compiler must be told about.
$runtime = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$references = 'System.Private.Windows.GdiPlus.dll', 'System.Private.Windows.Core.dll', 'System.Drawing.Common.dll',
    'System.Drawing.Primitives.dll', 'System.Runtime.dll', 'System.Collections.dll' |
    ForEach-Object { Join-Path $runtime $_ } | Where-Object { Test-Path $_ }

Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class ZarpazoIcon
{
    public static readonly int[] Sizes = { 16, 24, 32, 48, 64, 256 };

    // A claw mark: a pointed crescent from a to b, thickest in the middle, bowed sideways by `bend`.
    static GraphicsPath Slash(PointF a, PointF b, float bend, float width)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
        float nx = -dy / len, ny = dx / len;
        Func<float, float, PointF> at = (t, off) => new PointF(a.X + dx * t + nx * off, a.Y + dy * t + ny * off);
        var path = new GraphicsPath();
        path.AddBezier(a, at(0.33f, bend + width / 2), at(0.66f, bend + width / 2), b);
        path.AddBezier(b, at(0.66f, bend - width / 2), at(0.33f, bend - width / 2), a);
        path.CloseFigure();
        return path;
    }

    static GraphicsPath RoundedSquare(float size, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(size - d, 0, d, d, 270, 90);
        path.AddArc(size - d, size - d, d, d, 0, 90);
        path.AddArc(0, size - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // `simple` is for the tray sizes: only a thicker claw, because the viewfinder corners turn to mush at 16 px.
    static Bitmap Draw(int px, bool simple)
    {
        var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = px;

            using (var square = RoundedSquare(s, s * 0.22f))
            using (var back = new LinearGradientBrush(new PointF(0, 0), new PointF(0, s), Color.FromArgb(255, 44, 44, 56), Color.FromArgb(255, 24, 24, 32)))
                g.FillPath(back, square);

            if (!simple)
            {
                float m = s * 0.17f, len = s * 0.20f;
                using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), s * 0.055f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[] { new PointF(m, m + len), new PointF(m, m), new PointF(m + len, m) });
                    g.DrawLines(pen, new[] { new PointF(s - m - len, m), new PointF(s - m, m), new PointF(s - m, m + len) });
                    g.DrawLines(pen, new[] { new PointF(m, s - m - len), new PointF(m, s - m), new PointF(m + len, s - m) });
                    g.DrawLines(pen, new[] { new PointF(s - m - len, s - m), new PointF(s - m, s - m), new PointF(s - m, s - m - len) });
                }
            }

            using (var fire = new LinearGradientBrush(new PointF(s * 0.8f, 0), new PointF(s * 0.2f, s), Color.FromArgb(255, 255, 176, 59), Color.FromArgb(255, 229, 57, 53)))
            using (GraphicsPath claw = simple
                ? Slash(new PointF(s * 0.76f, s * 0.16f), new PointF(s * 0.24f, s * 0.84f), s * 0.07f, s * 0.27f)
                : Slash(new PointF(s * 0.74f, s * 0.22f), new PointF(s * 0.26f, s * 0.78f), s * 0.06f, s * 0.17f))
                g.FillPath(fire, claw);
        }
        return bmp;
    }

    // Drawn several times larger and scaled down, which keeps the edges smooth at 16 px.
    public static Bitmap Render(int px)
    {
        int factor = px <= 64 ? 8 : 2;
        using (Bitmap big = Draw(px * factor, px <= 24))
        {
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(big, 0, 0, px, px);
            }
            return bmp;
        }
    }

    // Entries below 256 are plain 32-bit bitmaps, which every Windows icon loader accepts; 256 is stored as PNG.
    static byte[] Entry(Bitmap bmp)
    {
        int px = bmp.Width;
        if (px >= 256)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        int stride = px * 4, maskStride = ((px + 31) / 32) * 4;
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, px, px), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rows = new byte[stride * px];
        for (int y = 0; y < px; y++) // bitmaps inside icons are stored bottom-up
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, rows, (px - 1 - y) * stride, stride);
        bmp.UnlockBits(data);

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(px); w.Write(px * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(rows.Length + maskStride * px); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            w.Write(rows);
            w.Write(new byte[maskStride * px]); // the alpha channel does the masking
            return ms.ToArray();
        }
    }

    public static void WriteIco(string path)
    {
        var entries = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
            using (Bitmap bmp = Render(Sizes[i]))
                entries[i] = Entry(bmp);

        using (var file = File.Create(path))
        using (var w = new BinaryWriter(file))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i])); w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(entries[i].Length); w.Write(offset);
                offset += entries[i].Length;
            }
            foreach (byte[] entry in entries) w.Write(entry);
        }
    }
}
'@

New-Item -ItemType Directory -Force -Path (Split-Path $OutIco) | Out-Null
[ZarpazoIcon]::WriteIco($OutIco)
"wrote $((Resolve-Path $OutIco).Path) ($((Get-Item $OutIco).Length) bytes)"

if ($PreviewPng) {
    # Reads the sizes back out of the .ico itself, so the preview shows what Windows will actually load.
    $sheet = [Drawing.Bitmap]::new(1040, 330)
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::FromArgb(255, 238, 238, 242))
    $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $font = [Drawing.Font]::new('Segoe UI', 9)
    $x = 15
    foreach ($size in [ZarpazoIcon]::Sizes) {
        $stream = [IO.File]::OpenRead($OutIco)
        $icon = [Drawing.Icon]::new($stream, [Drawing.Size]::new($size, $size))
        $stream.Dispose()
        $shown = [Math]::Min($size * 6, 280)
        if ($size -ge 64) { $shown = if ($size -eq 64) { 192 } else { 256 } }
        $g.FillRectangle([Drawing.Brushes]::White, $x - 4, 10, $shown + 8, 150)
        $g.DrawIcon($icon, [Drawing.Rectangle]::new($x, 14, $icon.Width, $icon.Height))
        $g.DrawString("$($icon.Width) px (real)", $font, [Drawing.Brushes]::DimGray, $x, 16 + [Math]::Max($icon.Height, 4) + 4)
        $big = $icon.ToBitmap()
        $g.DrawImage($big, $x, 175, [Math]::Min($shown, 150), [Math]::Min($shown, 150))
        $x += [Math]::Max($shown, 70) + 18
        $icon.Dispose()
    }
    $g.Dispose()
    $sheet.Save($PreviewPng, [Drawing.Imaging.ImageFormat]::Png)
    "preview $PreviewPng"
}
