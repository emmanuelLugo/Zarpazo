using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zarpazo.Guide;
using Zarpazo.Settings;
using Zarpazo.Shell;

namespace Zarpazo.Tests;

public sealed class GuideTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private static BitmapSource Image(int width, int height, byte shade = 0)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, shade);
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
    }

    private static Color PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        new CroppedBitmap(image, new System.Windows.Int32Rect(x, y, 1, 1)).CopyPixels(pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

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
    public void The_group_and_its_order_survive_closing_the_app()
    {
        Sta.Run(() =>
        {
            var before = new GuideDocument(_folder);
            GuideStep first = before.Add(Image(10, 10));
            GuideStep second = before.Add(Image(20, 10));
            before.Move(second, -1);

            var after = new GuideDocument(_folder);

            Assert.Equal([second.ImagePath, first.ImagePath], after.Steps.Select(step => step.ImagePath));
            Assert.Equal([1, 2], after.Steps.Select(step => step.Number));
        });
    }

    [Fact]
    public void A_group_that_was_emptied_stays_empty_after_restarting()
    {
        Sta.Run(() =>
        {
            var before = new GuideDocument(_folder);
            before.Add(Image(10, 10));
            before.Clear();

            Assert.Empty(new GuideDocument(_folder).Steps);
        });
    }

    [Fact]
    public void A_picture_that_was_deleted_meanwhile_is_left_out_and_strays_are_cleaned()
    {
        Sta.Run(() =>
        {
            var before = new GuideDocument(_folder);
            GuideStep gone = before.Add(Image(10, 10));
            GuideStep kept = before.Add(Image(10, 10));
            File.Delete(gone.ImagePath);
            string stray = Path.Combine(_folder, "stray.png");
            File.WriteAllText(stray, "not listed");

            var after = new GuideDocument(_folder);

            Assert.Equal([kept.ImagePath], after.Steps.Select(step => step.ImagePath));
            Assert.False(File.Exists(stray));
        });
    }

    [Fact]
    public void A_damaged_order_file_gives_an_empty_group_and_deletes_nothing()
    {
        Sta.Run(() =>
        {
            var before = new GuideDocument(_folder);
            string picture = before.Add(Image(10, 10)).ImagePath;
            File.WriteAllText(Path.Combine(_folder, "group.json"), "{ not json");

            var after = new GuideDocument(_folder);

            Assert.Empty(after.Steps);
            Assert.True(File.Exists(picture));
        });
    }

    [Fact]
    public void An_order_file_cannot_point_outside_the_folder()
    {
        Sta.Run(() =>
        {
            string outside = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", $"outside-{Guid.NewGuid():N}.png");
            Directory.CreateDirectory(_folder);
            File.WriteAllText(outside, "must not be touched");
            File.WriteAllText(Path.Combine(_folder, "group.json"), System.Text.Json.JsonSerializer.Serialize(new[] { "..\\" + Path.GetFileName(outside) }));
            try
            {
                var group = new GuideDocument(_folder);

                Assert.Empty(group.Steps);
                Assert.True(File.Exists(outside));
            }
            finally
            {
                File.Delete(outside);
            }
        });
    }

    [Fact]
    public void The_group_tells_when_it_changes_and_the_mode_is_not_kept_between_runs()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(_folder);
            int changes = 0;
            group.Changed += () => changes++;

            group.AddsOnCapture = true;
            group.AddsOnCapture = true; // no change, no notice
            GuideStep step = group.Add(Image(10, 10));
            group.Remove(step);

            Assert.Equal(3, changes);
            Assert.False(new GuideDocument(_folder).AddsOnCapture);
        });
    }

    [Fact]
    public void The_mode_ends_when_the_group_is_emptied_by_clearing_or_by_removing_the_last_capture()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(_folder);
            GuideStep first = group.Add(Image(10, 10));
            GuideStep second = group.Add(Image(10, 10));
            group.AddsOnCapture = true;

            group.Remove(first);
            Assert.True(group.AddsOnCapture); // one capture is still waiting

            group.Remove(second);
            Assert.False(group.AddsOnCapture);

            group.Add(Image(10, 10));
            group.AddsOnCapture = true;
            group.Clear();
            Assert.False(group.AddsOnCapture);
        });
    }

    [Fact]
    public void The_mode_turned_on_by_hand_with_an_empty_group_stays_on_until_used()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(_folder);

            group.AddsOnCapture = true;
            group.Clear(); // nothing to empty: still on

            Assert.True(group.AddsOnCapture);
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

            List<string> files = GuideShare.WriteFiles(group.Steps, share, numbered: false);

            Assert.Equal(["Captura 1.png", "Captura 2.png"], files.Select(Path.GetFileName));
            Assert.Equal(["Captura 1.png", "Captura 2.png"], Directory.EnumerateFiles(share).Select(Path.GetFileName).Order());
            Assert.Equal(group.Steps[0].ReadPng(), File.ReadAllBytes(files[0]));
        });
    }

    [Fact]
    public void With_numbering_each_file_of_a_group_gets_its_number_in_the_corner()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(Path.Combine(_folder, "group"));
            group.Add(Image(100, 80, 255));
            group.Add(Image(100, 80, 255));

            List<string> files = GuideShare.WriteFiles(group.Steps, Path.Combine(_folder, "share"), numbered: true);

            var first = new BitmapImage(); // read into memory, or the file stays locked and the folder can't be cleaned up
            first.BeginInit();
            first.CacheOption = BitmapCacheOption.OnLoad;
            first.UriSource = new Uri(files[0]);
            first.EndInit();
            Color corner = PixelAt(first, 14, 14); // inside the red badge
            Assert.True(corner.R > 200 && corner.G < 120, $"expected the red badge in the corner, got {corner}");
            Color far = PixelAt(first, 90, 70);
            Assert.Equal(Color.FromRgb(255, 255, 255), Color.FromRgb(far.R, far.G, far.B));
        });
    }

    [Fact]
    public void A_single_capture_is_never_numbered()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(Path.Combine(_folder, "group"));
            group.Add(Image(100, 80, 255));

            List<string> files = GuideShare.WriteFiles(group.Steps, Path.Combine(_folder, "share"), numbered: true);

            Assert.Equal(group.Steps[0].ReadPng(), File.ReadAllBytes(files[0]));
        });
    }

    [Fact]
    public void Joining_stacks_the_pictures_with_room_for_the_widest_and_the_sum_of_the_heights()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(Path.Combine(_folder, "group"));
            group.Add(Image(300, 100));
            group.Add(Image(500, 200));
            group.Add(Image(400, 50));

            BitmapSource joined = GuideShare.Stack(group.Steps, numbered: false);

            Assert.Equal(500 + 32, joined.PixelWidth); // 16 of margin on each side
            Assert.Equal(100 + 200 + 50 + 2 * 16 + 32, joined.PixelHeight); // pictures, the gaps between them, the margins
        });
    }

    [Fact]
    public void A_very_long_group_is_shrunk_to_fit_the_renderer()
    {
        Sta.Run(() =>
        {
            var group = new GuideDocument(Path.Combine(_folder, "group"));
            for (int i = 0; i < 4; i++)
            {
                group.Add(Image(200, 6000));
            }

            BitmapSource joined = GuideShare.Stack(group.Steps, numbered: false);

            Assert.True(joined.PixelHeight <= 16000, $"{joined.PixelHeight} is taller than the renderer accepts");
        });
    }

    [Fact]
    public void The_group_shortcut_has_a_default_that_differs_from_the_others_and_a_clash_is_resolved()
    {
        var settings = new AppSettings();
        Assert.Equal(HotKeyBinding.DefaultGroup, settings.GroupHotKey);
        Assert.NotEqual(HotKeyBinding.Default, HotKeyBinding.DefaultGroup);
        Assert.NotEqual(HotKeyBinding.DefaultRecord, HotKeyBinding.DefaultGroup);

        settings.GroupHotKey = settings.RecordHotKey; // set by hand to the recording shortcut
        settings.Normalize();
        Assert.Equal(HotKeyBinding.DefaultGroup, settings.GroupHotKey);

        settings.GroupHotKey = new HotKeyBinding(0, 0x43); // the letter C alone would be taken from every program
        settings.Normalize();
        Assert.Equal(HotKeyBinding.DefaultGroup, settings.GroupHotKey);
    }
}
