using System.Text;

namespace Zarpazo.Ocr;

/// <summary>Rebuilds the indentation and blank lines of recognized text from where each line sits in the image.</summary>
internal static class TextLayout
{
    // Two lines whose tops are this many times the usual line pitch apart have an empty line between them.
    private const double BlankLineFactor = 1.6;

    public static string Format(IReadOnlyList<OcrLineBox> lines)
    {
        if (lines.Count == 0)
        {
            return "";
        }

        double charWidth = CharWidth(lines);
        double pitch = Median(lines.Zip(lines.Skip(1), (a, b) => Middle(b) - Middle(a)).Where(step => step > 0).ToList());
        double margin = lines.Min(line => line.Left);

        var result = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                result.Append('\n');
                if (pitch > 0 && Middle(lines[i]) - Middle(lines[i - 1]) >= pitch * BlankLineFactor)
                {
                    result.Append('\n');
                }
            }

            // Less than half a character off is noise, not an indent; proportional fonts rarely line up exactly.
            int indent = charWidth > 0 ? (int)Math.Round((lines[i].Left - margin) / charWidth) : 0;
            result.Append(' ', Math.Max(0, indent)).Append(lines[i].Text);
        }
        return result.ToString();
    }

    // The vertical center moves less than the top, which depends on whether the line has tall or hanging letters.
    private static double Middle(OcrLineBox line) => line.Top + line.Height / 2;

    // Exact for monospaced text, which is where indentation matters; a fair average for everything else.
    private static double CharWidth(IReadOnlyList<OcrLineBox> lines)
    {
        var widths = lines.Where(line => line.Text.Length >= 3).Select(line => line.Width / line.Text.Length).ToList();
        return widths.Count > 0 ? Median(widths) : Median(lines.Select(line => line.Height * 0.5).ToList());
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        return values[(values.Count - 1) / 2]; // the lower one when even: a blank line must not stretch the usual pitch
    }
}
