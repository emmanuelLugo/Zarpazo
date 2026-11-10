using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Guide;

namespace Zarpazo.Tests;

public sealed class GuideProjectTests : IDisposable
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

    private string Project => Path.Combine(_folder, "guia" + GuideProject.Extension);

    [Fact]
    public void A_saved_guide_opens_with_its_title_texts_order_and_pictures()
    {
        Sta.Run(() =>
        {
            var original = new GuideDocument(Path.Combine(_folder, "a")) { Title = "Alta de cliente" };
            original.Add(Image(30, 20)).Text = "Abre el menú";
            original.Add(Image(50, 40)).Text = "Línea 1\nLínea 2";
            GuideProject.Save(original, Project);

            var reopened = new GuideDocument(Path.Combine(_folder, "b"));
            GuideProject.Load(reopened, Project);

            Assert.Equal("Alta de cliente", reopened.Title);
            Assert.Equal(["Abre el menú", "Línea 1\nLínea 2"], reopened.Steps.Select(step => step.Text));
            Assert.Equal([1, 2], reopened.Steps.Select(step => step.Number));
            Assert.Equal([30, 50], reopened.Steps.Select(step => step.LoadImage().PixelWidth));
        });
    }

    [Fact]
    public void Opening_replaces_what_the_guide_had()
    {
        Sta.Run(() =>
        {
            var saved = new GuideDocument(Path.Combine(_folder, "a"));
            saved.Add(Image(10, 10)).Text = "nuevo";
            GuideProject.Save(saved, Project);

            var guide = new GuideDocument(Path.Combine(_folder, "b")) { Title = "viejo" };
            string oldPicture = guide.Add(Image(10, 10)).ImagePath;
            GuideProject.Load(guide, Project);

            Assert.Equal(["nuevo"], guide.Steps.Select(step => step.Text));
            Assert.Equal("", guide.Title);
            Assert.False(File.Exists(oldPicture));
        });
    }

    [Fact]
    public void A_file_that_is_not_a_guide_is_refused_and_the_guide_is_left_alone()
    {
        Sta.Run(() =>
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Project, "esto no es un zip");
            var guide = new GuideDocument(Path.Combine(_folder, "b")) { Title = "intacta" };
            guide.Add(Image(10, 10));

            Assert.Throws<InvalidDataException>(() => GuideProject.Load(guide, Project));

            Assert.Equal("intacta", guide.Title);
            Assert.Single(guide.Steps);
        });
    }

    [Fact]
    public void A_zip_without_a_picture_it_lists_is_refused_and_the_guide_is_left_alone()
    {
        Sta.Run(() =>
        {
            Directory.CreateDirectory(_folder);
            using (var zip = ZipFile.Open(Project, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("guide.json").Open()))
            {
                writer.Write("""{"Version":1,"Title":"x","Steps":[{"Text":"a","Image":"steps/001.png"}]}""");
            }
            var guide = new GuideDocument(Path.Combine(_folder, "b")) { Title = "intacta" };

            Assert.Throws<InvalidDataException>(() => GuideProject.Load(guide, Project));

            Assert.Equal("intacta", guide.Title);
        });
    }

    [Fact]
    public void A_guide_from_a_newer_version_is_refused()
    {
        Sta.Run(() =>
        {
            Directory.CreateDirectory(_folder);
            using (var zip = ZipFile.Open(Project, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("guide.json").Open()))
            {
                writer.Write("""{"Version":99,"Title":"x","Steps":[]}""");
            }

            Assert.Throws<InvalidDataException>(() => GuideProject.Load(new GuideDocument(Path.Combine(_folder, "b")), Project));
        });
    }

    [Fact]
    public void Saving_leaves_no_partial_file_beside_the_project()
    {
        Sta.Run(() =>
        {
            var guide = new GuideDocument(Path.Combine(_folder, "a"));
            guide.Add(Image(10, 10));
            GuideProject.Save(guide, Project);
            GuideProject.Save(guide, Project); // over an existing one

            Assert.Equal([Project], Directory.EnumerateFiles(_folder));
        });
    }
}
