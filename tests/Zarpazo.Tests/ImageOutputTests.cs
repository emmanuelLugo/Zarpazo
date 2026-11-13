using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Output;

namespace Zarpazo.Tests;

/// <summary>Only the file side: touching the real clipboard would overwrite whatever the person had copied.</summary>
public sealed class ImageOutputTests : IDisposable
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
    public void Writes_a_png_that_decodes_to_the_same_size_and_has_no_spaces_in_its_path()
    {
        string path = ImageOutput.SaveForPaste(Image(30, 20), _folder);

        Assert.DoesNotContain(' ', Path.GetFileName(path));
        using FileStream file = File.OpenRead(path);
        BitmapFrame decoded = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal((30, 20), (decoded.PixelWidth, decoded.PixelHeight));
    }

    [Fact]
    public void Removes_files_older_than_a_week_and_keeps_recent_ones_and_the_new_one()
    {
        Directory.CreateDirectory(_folder);
        string old = Path.Combine(_folder, "old.png");
        string recent = Path.Combine(_folder, "recent.png");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-8));
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-6));

        string created = ImageOutput.SaveForPaste(Image(4, 4), _folder);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(created));
    }

    [Fact]
    public void Two_captures_in_a_row_do_not_overwrite_each_other()
    {
        string first = ImageOutput.SaveForPaste(Image(4, 4), _folder);
        string second = ImageOutput.SaveForPaste(Image(4, 4), _folder);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_pasted_file_takes_the_name_from_the_template_without_spaces()
    {
        string name = FileNameTemplate.ForPaste("Cap {fecha} {hora}", new DateTime(2026, 11, 11, 15, 2, 57), 1);

        string path = ImageOutput.SaveForPaste(Image(4, 4), _folder, name);

        Assert.Equal("Cap_2026-11-11_15-02-57.png", Path.GetFileName(path));
    }

    [Fact]
    public void Two_pasted_files_with_the_same_name_do_not_overwrite_each_other()
    {
        string first = ImageOutput.SaveForPaste(Image(4, 4), _folder, "Cap_x");
        string second = ImageOutput.SaveForPaste(Image(4, 4), _folder, "Cap_x");

        Assert.Equal("Cap_x_2.png", Path.GetFileName(second));
        Assert.True(File.Exists(first));
    }
}
