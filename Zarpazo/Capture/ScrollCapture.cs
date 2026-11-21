using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Capture;

/// <summary>How a scroll capture ended: the picture (null if there is none) and a note for the person when it stopped short or failed.</summary>
internal sealed record ScrollResult(BitmapSource? Image, string? Notice);

/// <summary>
/// Scrolls whatever is under an area of the screen with the mouse wheel and joins the pictures taken after each step into one tall one.
/// It stops at the end of the content, when Esc is pressed, or at <see cref="MaxHeight"/>.
/// </summary>
internal static class ScrollCapture
{
    /// <summary>Tallest picture made; 20000 rows of a 1080p-wide area is about 150 MB in memory.</summary>
    public const int MaxHeight = 20000;

    private const int WheelNotch = 120;
    private const int EscapeKey = 0x1B;
    private const int FirstNotches = 1;
    private const int MaxNotches = 30;
    private const double TargetShare = 0.5;   // each step should move about half the area, so the pictures overlap well
    private const int SettleFirstMs = 150;
    private const int SettleRetryMs = 70;
    private const int SettleTries = 14;       // smooth scrolling takes a moment to stop; about a second at most
    private const int MaxSteps = 600;
    private const uint MouseEventWheel = 0x0800;

    /// <param name="region">The area, in physical screen pixels. The overlay must already be gone.</param>
    /// <param name="progress">Told the height of the picture after each step.</param>
    public static async Task<ScrollResult> RunAsync(Rectangle region, IProgress<int>? progress = null)
    {
        var input = new WindowsInputState();
        input.WasPressedSinceLastCheck(EscapeKey); // clears an old press, so only a new one stops it

        System.Drawing.Point cursorBefore = System.Windows.Forms.Cursor.Position;
        // The wheel scrolls what is under the pointer, so the pointer goes to the middle of the area.
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(region.X + region.Width / 2, region.Y + region.Height / 2);
        try
        {
            using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            var stitcher = new ScrollStitcher(region.Width, region.Height);
            var frame = new byte[region.Width * region.Height * 4];

            await Task.Delay(SettleFirstMs); // the pointer arriving can change what is under it (a hover)
            Grab(region, bitmap, frame);
            stitcher.Add(frame);
            progress?.Report(stitcher.TotalHeight);

            int notches = FirstNotches;
            for (int step = 0; step < MaxSteps; step++)
            {
                if (input.WasPressedSinceLastCheck(EscapeKey))
                {
                    return Done(stitcher, null);
                }
                if (stitcher.TotalHeight >= MaxHeight)
                {
                    return Done(stitcher, "Se llegó al largo máximo; la captura se cortó ahí.");
                }

                frame = await ScrollAndSettleAsync(region, bitmap, frame.Length, notches);
                ScrollStep result = stitcher.Add(frame);
                if (result == ScrollStep.End)
                {
                    return stitcher.TotalHeight == region.Height
                        ? new ScrollResult(null, "Esa zona no se desplaza. Elige una zona con contenido que baje con la rueda del ratón.")
                        : Done(stitcher, null);
                }
                if (result == ScrollStep.Lost)
                {
                    return stitcher.TotalHeight == region.Height
                        ? new ScrollResult(null, "No se pudo unir el desplazamiento. Prueba con una zona más alta o sin animaciones ni videos.")
                        : Done(stitcher, "Se perdió el alineado (una animación o un salto); la captura llega hasta ahí.");
                }

                progress?.Report(stitcher.TotalHeight);
                // One notch moves a fixed amount, so after the first step the number of notches that moves half the area is known.
                if (step == 0 && stitcher.LastShift > 0)
                {
                    double perNotch = (double)stitcher.LastShift / notches;
                    notches = Math.Clamp((int)Math.Round(TargetShare * region.Height / perNotch), 1, MaxNotches);
                }
            }
            return Done(stitcher, "Se llegó al máximo de pasos; la captura se cortó ahí.");
        }
        finally
        {
            System.Windows.Forms.Cursor.Position = cursorBefore;
        }
    }

    private static ScrollResult Done(ScrollStitcher stitcher, string? notice) => new(stitcher.Result(), notice);

    // Turns the wheel, then waits until the area stops changing: scrolling is often animated, and a picture taken halfway can't be lined up.
    private static async Task<byte[]> ScrollAndSettleAsync(Rectangle region, Bitmap bitmap, int size, int notches)
    {
        for (int i = 0; i < notches; i++)
        {
            mouse_event(MouseEventWheel, 0, 0, -WheelNotch, UIntPtr.Zero);
        }
        await Task.Delay(SettleFirstMs);

        var current = new byte[size];
        Grab(region, bitmap, current);
        for (int tries = 0; tries < SettleTries; tries++)
        {
            await Task.Delay(SettleRetryMs);
            var next = new byte[size];
            Grab(region, bitmap, next);
            bool settled = ScrollStitcher.Same(current, next, region.Width, region.Height);
            current = next;
            if (settled)
            {
                break;
            }
        }
        return current;
    }

    private static void Grab(Rectangle region, Bitmap bitmap, byte[] into)
    {
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty, region.Size); // no pointer in it
        }

        BitmapData data = bitmap.LockBits(new Rectangle(System.Drawing.Point.Empty, region.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < region.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, into, y * region.Width * 4, region.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extraInfo);
}
