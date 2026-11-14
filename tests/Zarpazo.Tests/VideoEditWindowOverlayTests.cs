using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>The blurs, texts and arrows in the video editor's window: their row, their panel and their preview over the picture.</summary>
[Collection("Recording")]
public sealed class VideoEditWindowOverlayTests : IDisposable
{
    private const int Width = 640;
    private const int Height = 360;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoEditWindowOverlayTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static bool Pump(Func<bool> until, int milliseconds = 15000)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            if (until())
            {
                return true;
            }

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
        return until();
    }

    // Three seconds of a page: white, with lines of small black print and a blue bar on top. A blur over the print turns it grey.
    private async Task<string> PageVideoAsync()
    {
        string path = Path.Combine(_folder, "page.mp4");
        var writer = new Mp4Writer(path, Width, Height, fps: 30, withAudio: false, live: false);
        writer.Start();
        for (int frame = 0; frame < 90; frame++)
        {
            byte[] pixels = writer.RentPicture();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte b = 255, g = 255, r = 255;
                    if (y < 40)
                    {
                        (b, g, r) = (200, 120, 40);
                    }
                    else if (y % 14 < 6 && x > 40 && x < 600 && x % 7 < 4)
                    {
                        b = g = r = 30;
                    }
                    int i = (y * Width + x) * 4;
                    pixels[i] = b;
                    pixels[i + 1] = g;
                    pixels[i + 2] = r;
                    pixels[i + 3] = 255;
                }
            }
            await writer.AddVideoAsync(pixels, TimeSpan.FromSeconds(frame / 30.0));
        }
        await writer.FinishAsync();
        return path;
    }

    private static void Shot(Window window, string? folder, string name)
    {
        window.UpdateLayout();
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var visual = (Grid)window.Content;
        visual.Background = window.Background;
        var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using FileStream stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }

    [NeedsGpuFact]
    public void Overlays_show_in_the_preview_only_while_they_last_and_make_the_video_worth_saving()
    {
        string video = PageVideoAsync().GetAwaiter().GetResult();
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(video);
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zarpazo;component/Theme/Dark.xaml", UriKind.Relative) });
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), $"the video should open: {window.KeptText.Text}");
                EditTimeline timeline = window.Timeline!;
                Assert.False(window.NeedsRendering);

                timeline.AddOverlay(new VideoOverlay(OverlayKind.Blur, 0, 2, 0.1, 0.25, 0.6, 0.55));
                timeline.AddOverlay(new VideoOverlay(OverlayKind.Text, 0, 2.5, 0.12, 0.65, Text: "Aquí van los datos", Size: 0.07));
                timeline.AddOverlay(new VideoOverlay(OverlayKind.Arrow, 0, 3, 0.8, 0.8, 0.65, 0.4));
                timeline.AddOverlay(new VideoOverlay(OverlayKind.Text, 2.4, 3, 0.1, 0.1, Text: "Más tarde"));
                Pump(() => false, 300);

                Assert.True(window.NeedsRendering, "overlays are drawn in again when the video is saved");
                Assert.True(timeline.HasChanges);
                Assert.Equal(4, window.OverlayCanvas.Children.Count);
                Assert.Equal(Visibility.Visible, window.OverlayCanvas.Children[0].Visibility);
                Assert.Equal(Visibility.Collapsed, window.OverlayCanvas.Children[3].Visibility); // it starts at 2.4 s

                window.OverlayLane.SelectedOverlay = 0;
                Pump(() => false, 600);
                Assert.True(window.OverlayTab.IsChecked, "choosing a block opens the panel of the overlays");
                Assert.Equal(Visibility.Visible, window.OverlayDetails.Visibility);
                Assert.Equal(Visibility.Visible, window.OverlayHandles.Visibility);
                Shot(window, folder, "overlays-blur.png");

                window.OverlayLane.SelectedOverlay = 1;
                Pump(() => false, 600);
                Assert.Equal("Aquí van los datos", window.OverlayTextBox.Text);
                Shot(window, folder, "overlays-text.png");

                window.OverlayLane.SelectedOverlay = 2;
                Pump(() => false, 600);
                Assert.Equal(Visibility.Collapsed, window.OverlayTextBlock.Visibility);
                Shot(window, folder, "overlays-arrow.png");

                window.OverlayLane.SelectedOverlay = -1;
                timeline.Undo();
                timeline.Undo();
                timeline.Undo();
                timeline.Undo();
                Pump(() => false, 300);
                Assert.Empty(timeline.Overlays);
                Assert.Empty(window.OverlayCanvas.Children);
                Assert.False(window.NeedsRendering);
            }
            finally
            {
                // Closing with changes asks whether to throw them away: a failed check above would leave that question open for ever.
                while (window.Timeline?.CanUndo == true)
                {
                    window.Timeline.Undo();
                }
                window.Close();
            }
        });
    }

    [Fact]
    public void A_handle_dragged_to_a_point_moves_that_corner_or_end_and_keeps_the_rest()
    {
        var blur = new VideoOverlay(OverlayKind.Blur, 0, 2, 0.2, 0.2, 0.6, 0.6);
        var arrow = new VideoOverlay(OverlayKind.Arrow, 0, 2, 0.2, 0.2, 0.6, 0.6);

        Assert.Equal((0.1, 0.3, 0.6, 0.6), Corners(VideoEditWindow.DragHandle(blur, 0, 0.1, 0.3)));
        Assert.Equal((0.2, 0.3, 0.7, 0.6), Corners(VideoEditWindow.DragHandle(blur, 1, 0.7, 0.3)));
        Assert.Equal((0.2, 0.2, 0.6, 0.9), Corners(VideoEditWindow.DragHandle(blur, 3, 0.6, 0.9)));
        Assert.Equal((0.5, 0.5, 0.6, 0.6), Corners(VideoEditWindow.DragHandle(arrow, 0, 0.5, 0.5)));
        Assert.Equal((0.2, 0.2, 0.9, 0.1), Corners(VideoEditWindow.DragHandle(arrow, 1, 0.9, 0.1)));
    }

    private static (double, double, double, double) Corners(VideoOverlay overlay) => (overlay.X, overlay.Y, overlay.X2, overlay.Y2);

    [Fact]
    public void Blocks_that_overlap_go_on_rows_of_their_own_and_those_that_do_not_share_one()
    {
        OverlaySpan[] spans = [new(0, 2, 0), new(1, 3, 1), new(2, 4, 2), new(1.5, 1.8, 3)];

        IReadOnlyList<int> rows = OverlayLaneView.Rows(spans);

        Assert.Equal([0, 1, 0, 2], rows);
        Assert.True(OverlayLaneView.HeightFor(3) > OverlayLaneView.HeightFor(1));
    }

    [Fact]
    public void A_block_is_found_by_its_row_and_its_sides_by_their_edges()
    {
        OverlaySpan[] spans = [new(0, 2, 0), new(1, 3, 1)];
        IReadOnlyList<int> rows = OverlayLaneView.Rows(spans);
        double pps = 100;
        double second = TimelineLayout.XOf(1.5, pps); // inside both

        // The first row holds the first block; the second row, the second.
        (OverlayLaneView.Part part, int span) = OverlayLaneView.HitTest(spans, rows, pps, new Point(second, 4 + 5));
        Assert.Equal((OverlayLaneView.Part.Body, 0), (part, span));
        (part, span) = OverlayLaneView.HitTest(spans, rows, pps, new Point(second, 4 + OverlayLaneView.RowPitch + 5));
        Assert.Equal((OverlayLaneView.Part.Body, 1), (part, span));
        (part, span) = OverlayLaneView.HitTest(spans, rows, pps, new Point(TimelineLayout.XOf(0, pps) + 1, 9));
        Assert.Equal((OverlayLaneView.Part.StartEdge, 0), (part, span));
        (part, span) = OverlayLaneView.HitTest(spans, rows, pps, new Point(TimelineLayout.XOf(3, pps) - 1, 4 + OverlayLaneView.RowPitch + 5));
        Assert.Equal((OverlayLaneView.Part.EndEdge, 1), (part, span));
        Assert.Equal(OverlayLaneView.Part.None, OverlayLaneView.HitTest(spans, rows, pps, new Point(TimelineLayout.XOf(5, pps), 9)).Part);
    }
}
