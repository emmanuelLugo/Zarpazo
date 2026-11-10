using System.IO;
using System.Text;

namespace Zarpazo.Guide;

/// <summary>The smallest PDF that holds full-page JPEG pictures: one A4 page per picture. No library needed.</summary>
internal static class PdfWriter
{
    private const int PageWidthPoints = 595;
    private const int PageHeightPoints = 842;

    /// <param name="pages">One JPEG per page, with its size in pixels.</param>
    public static void Write(Stream output, IReadOnlyList<(byte[] Jpeg, int Width, int Height)> pages)
    {
        // Objects: 1 catalog, 2 page list, then for page i: 3+3i the page, 4+3i its drawing instructions, 5+3i its picture.
        var offsets = new List<long>();
        long position = 0;

        void Put(string text) => PutBytes(Encoding.Latin1.GetBytes(text));
        void PutBytes(byte[] bytes)
        {
            output.Write(bytes);
            position += bytes.Length;
        }
        void Begin(int id)
        {
            offsets.Add(position);
            Put($"{id} 0 obj\n");
        }

        Put("%PDF-1.4\n");
        Begin(1);
        Put("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        Begin(2);
        string kids = string.Join(" ", pages.Select((_, i) => $"{3 + 3 * i} 0 R"));
        Put($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (int i = 0; i < pages.Count; i++)
        {
            int page = 3 + 3 * i;
            Begin(page);
            Put($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidthPoints} {PageHeightPoints}] " +
                $"/Resources << /XObject << /Im0 {page + 2} 0 R >> >> /Contents {page + 1} 0 R >>\nendobj\n");

            string draw = $"q {PageWidthPoints} 0 0 {PageHeightPoints} 0 0 cm /Im0 Do Q";
            Begin(page + 1);
            Put($"<< /Length {draw.Length} >>\nstream\n{draw}\nendstream\nendobj\n");

            (byte[] jpeg, int width, int height) = pages[i];
            Begin(page + 2);
            Put($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB " +
                $"/BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            PutBytes(jpeg);
            Put("\nendstream\nendobj\n");
        }

        long xref = position;
        Put($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            Put($"{offset:D10} 00000 n \n");
        }
        Put($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
