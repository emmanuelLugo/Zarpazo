using System.IO;
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
    public void Captures_are_numbered_in_order_and_renumbered_after_a_move_or_removal()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(_folder);
            GuideStep first = group.Add(Image(10, 10));
            GuideStep second = group.Add(Image(10, 10));
            GuideStep third = group.Add(Image(10, 10));

            group.Move(third, -2);
            Assert.Equal([third, first, second], group.Steps);
            Assert.Equal([1, 2, 3], group.Steps.Select(step => step.Number));

            group.Remove(first);
            Assert.Equal([third, second], group.Steps);
            Assert.Equal([1, 2], group.Steps.Select(step => step.Number));
            Assert.False(File.Exists(first.ImagePath));
        });
    }

    [Fact]
    public void Moving_past_the_ends_stops_at_the_end()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(_folder);
            GuideStep first = group.Add(Image(10, 10));
            GuideStep second = group.Add(Image(10, 10));

            group.Move(first, -5);
            group.Move(second, 5);

            Assert.Equal([first, second], group.Steps);
        });
    }

    [Fact]
    public void Clear_removes_the_captures_and_their_pictures()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(_folder);
            string path = group.Add(Image(10, 10)).ImagePath;

            group.Clear();

            Assert.Empty(group.Steps);
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void Copying_writes_one_file_per_capture_named_after_it_and_drops_the_old_ones()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(Path.Combine(_folder, "group"));
            group.Add(Image(10, 10));
            group.Add(Image(10, 10));
            string share = Path.Combine(_folder, "share");
            Directory.CreateDirectory(share);
            File.WriteAllText(Path.Combine(share, "Captura 9.png"), "left over from an earlier copy");

            List<string> files = GuideShare.WriteFiles(group.Steps, share);

            Assert.Equal(["Captura 1.png", "Captura 2.png"], files.Select(Path.GetFileName));
            Assert.Equal(["Captura 1.png", "Captura 2.png"], Directory.EnumerateFiles(share).Select(Path.GetFileName).Order());
            Assert.Equal(group.Steps[0].ReadPng(), File.ReadAllBytes(files[0]));
        });
    }
}
