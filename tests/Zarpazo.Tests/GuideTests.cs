using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Guide;

namespace Zarpazo.Tests;

public sealed class GuideTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private static BitmapSource Image(int width, int height) =>
        BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[width * height * 4], width * 4);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Steps_are_numbered_in_order_and_renumbered_after_a_move_or_removal()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder);
            GuideStep first = guide.Add(Image(10, 10));
            GuideStep second = guide.Add(Image(10, 10));
            GuideStep third = guide.Add(Image(10, 10));

            guide.Move(third, -2);
            Assert.Equal([third, first, second], guide.Steps);
            Assert.Equal([1, 2, 3], guide.Steps.Select(step => step.Number));

            guide.Remove(first);
            Assert.Equal([third, second], guide.Steps);
            Assert.Equal([1, 2], guide.Steps.Select(step => step.Number));
            Assert.False(File.Exists(first.ImagePath));
        });
    }

    [Fact]
    public void Moving_past_the_ends_stops_at_the_end()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder);
            GuideStep first = guide.Add(Image(10, 10));
            GuideStep second = guide.Add(Image(10, 10));

            guide.Move(first, -5);
            guide.Move(second, 5);

            Assert.Equal([first, second], guide.Steps);
        });
    }

    [Fact]
    public void Clear_removes_the_steps_the_title_and_the_pictures()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder) { Title = "Alta de cliente" };
            string path = guide.Add(Image(10, 10)).ImagePath;

            guide.Clear();

            Assert.Empty(guide.Steps);
            Assert.Equal("", guide.Title);
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void Html_has_the_title_every_step_and_escapes_what_was_typed()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder);
            guide.Add(Image(10, 10)).Text = "Abre <Ajustes> & elige \"Red\"";
            guide.Add(Image(10, 10));

            string html = GuideHtml.Build("Cómo conectarse", guide.Steps);

            Assert.Contains("<h1>C&#243;mo conectarse</h1>", html);
            Assert.Contains("Paso 1", html);
            Assert.Contains("Paso 2", html);
            Assert.Contains("Abre &lt;Ajustes&gt; &amp; elige &quot;Red&quot;", html);
            Assert.Equal(2, html.Split("data:image/png;base64,").Length - 1);
        });
    }

    [Fact]
    public void Pdf_has_one_page_for_a_short_guide_and_is_well_formed()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder) { Title = "Prueba" };
            guide.Add(Image(400, 300)).Text = "Primero haz clic aquí.";
            guide.Add(Image(400, 300));

            string pdf = Render(guide);

            Assert.StartsWith("%PDF-1.4", pdf);
            Assert.Contains("/Count 1 ", pdf);
            Assert.EndsWith("%%EOF\n", pdf);
        });
    }

    [Fact]
    public void Pdf_moves_to_a_new_page_when_the_next_step_does_not_fit()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder);
            for (int i = 0; i < 6; i++)
            {
                guide.Add(Image(1200, 700)); // about 400 units tall once fitted to the page: two per page
            }

            Assert.Contains("/Count 3 ", Render(guide));
        });
    }

    [Fact]
    public void Pdf_shrinks_a_picture_taller_than_a_page_instead_of_cutting_it()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder);
            guide.Add(Image(500, 5000));

            Assert.Contains("/Count 2 ", Render(guide)); // the title, and the step on a page of its own
        });
    }

    [Fact]
    public void Pdf_xref_offsets_point_at_their_objects()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(_folder);
            guide.Add(Image(300, 200));

            byte[] bytes = RenderBytes(guide);
            string text = Encoding.Latin1.GetString(bytes);
            int xref = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1; // not the "startxref" after it
            string[] lines = text[xref..].Split('\n');
            int objects = int.Parse(lines[1].Split(' ')[1]) - 1;
            for (int id = 1; id <= objects; id++)
            {
                int offset = int.Parse(lines[2 + id].Split(' ')[0]);
                Assert.StartsWith($"{id} 0 obj", text[offset..]);
            }
        });
    }

    private static string Render(GuideDocument guide) => Encoding.Latin1.GetString(RenderBytes(guide));

    private static byte[] RenderBytes(GuideDocument guide)
    {
        using var stream = new MemoryStream();
        PdfWriter.Write(stream, GuidePages.Render(guide.Title, guide.Steps));
        return stream.ToArray();
    }
}
