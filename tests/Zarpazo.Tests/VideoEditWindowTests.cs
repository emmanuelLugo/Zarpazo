using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

[Collection("Recording")]
public sealed class VideoEditWindowTests : IClassFixture<SampleVideos>, IDisposable
{
    private readonly SampleVideos _samples;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public VideoEditWindowTests(SampleVideos samples)
    {
        _samples = samples;
        Directory.CreateDirectory(_folder);
    }

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

    // Lets the window do its work (opening the video, drawing) for a while, or until the condition holds.
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

    [Fact]
    public void The_window_can_be_created_without_failing_while_its_controls_load()
    {
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(Path.Combine(_folder, "not-there.mp4")); // opening the file fails later, asynchronously

            Assert.False(window.SaveButton.IsEnabled); // nothing can be saved before the video has opened
            Assert.Null(window.EditedPath);
            window.Close();
        });
    }

    [Fact]
    public void Once_the_video_opens_it_can_be_edited_and_the_buttons_follow_the_edit()
    {
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(_samples.Silent);
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                EditTimeline timeline = window.Timeline!;
                Assert.InRange(timeline.SourceDuration, 2.5, 3.5);
                Assert.False(window.SaveButton.IsEnabled, "nothing to save before an edit");

                timeline.Split(1);
                timeline.Split(2);
                Pump(() => false, 100);
                Assert.True(window.SaveButton.IsEnabled);
                Assert.True(window.UndoButton.IsEnabled);
                Assert.True(window.ResetButton.IsEnabled);

                timeline.Move(2, 0);
                timeline.Delete(1);
                timeline.SetMuted(0, true);
                timeline.TrimEnd(0, 3);
                Pump(() => false, 100);
                Assert.Equal(2, timeline.Segments.Count);

                timeline.Reset();
                Pump(() => false, 100);
                Assert.False(window.SaveButton.IsEnabled, "back to the whole video, nothing to save");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void An_edited_video_is_saved_next_to_the_recording_without_touching_it()
    {
        string copy = Path.Combine(_folder, "recording.mp4");
        File.Copy(_samples.Silent, copy);

        Sta.Run(() =>
        {
            var window = new VideoEditWindow(copy);
            // The dialog blocks, so a timer does the editing once the video has opened, and another gives up if nothing happens.
            var driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (_, _) =>
            {
                if (window.Timeline == null)
                {
                    return;
                }
                driver.Stop();
                window.Timeline.Split(1);
                window.Timeline.Delete(0);
                window.SaveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            var giveUp = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
            giveUp.Tick += (_, _) =>
            {
                giveUp.Stop();
                window.Close();
            };
            driver.Start();
            giveUp.Start();
            Assert.True(window.ShowDialog());
            Assert.Equal(Path.Combine(_folder, "recording_editado.mp4"), window.EditedPath);
            Assert.True(File.Exists(window.EditedPath));
            Assert.True(File.Exists(copy));
        });
    }

    [Fact]
    public void A_recording_with_clicks_opens_with_its_zooms_ready_to_save_without_asking_to_discard_them()
    {
        string copy = Path.Combine(_folder, "clicked.mp4");
        File.Copy(_samples.Silent, copy);
        new CursorTrack(320, 180, [new CursorSample(0, 80, 45)], [new CursorClick(1500, 1600, 80, 45, ClickButton.Left)]).Save(copy);

        Sta.Run(() =>
        {
            var window = new VideoEditWindow(copy);
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                ZoomRegion zoom = Assert.Single(window.Timeline!.Zooms);
                Assert.Equal((0.25, 0.25), (zoom.FocusX, zoom.FocusY));
                Pump(() => false, 100);
                Assert.True(window.SaveButton.IsEnabled, "the zooms alone make a different video");
                Assert.False(window.UndoButton.IsEnabled, "the automatic zooms are where the edit starts");
                Assert.True(window.AutoZoomButton.IsEnabled);

                window.Lane.SelectedZoom = 0;
                Pump(() => false, 100);
                Assert.Equal(Visibility.Visible, window.ZoomDetails.Visibility);
                Assert.Equal(Visibility.Visible, window.FocusLayer.Visibility); // the whole picture, to choose where it looks
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void An_older_recording_without_clicks_opens_without_zooms_and_can_still_get_one_by_hand()
    {
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(_samples.Silent);
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                Assert.Empty(window.Timeline!.Zooms);
                Assert.False(window.AutoZoomButton.IsEnabled);

                window.AddZoomButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => false, 100);
                Assert.Single(window.Timeline.Zooms);
                Assert.True(window.UndoButton.IsEnabled);
                Assert.True(window.Timeline.HasChanges);

                window.Timeline.Undo(); // or closing would ask whether to throw the zoom away
                Assert.False(window.Timeline.HasChanges);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // A picture of the window for looking at it: only when the environment variable names a folder.
    [NeedsGpuFact]
    public void The_window_draws_the_timeline_with_pictures_of_the_video()
    {
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(_samples.WithSound);
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zarpazo;component/Theme/Dark.xaml", UriKind.Relative) });
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null));
                Assert.True(Pump(() => window.View.ThumbnailCount >= 6), "the blocks should fill with pictures of the video");
                EditTimeline timeline = window.Timeline!;
                timeline.Split(0.8);
                timeline.Split(1.9);
                timeline.Move(2, 0);
                timeline.SetMuted(1, true);
                window.View.SelectedIndex = 1;
                window.View.Playhead = 1.2;
                timeline.AddZoom(new ZoomRegion(0.3, 1.4, 2, 0.7, 0.3));
                Pump(() => false, 500);
                Shot(window, folder, "editor.png");

                window.Lane.SelectedZoom = 0;
                Pump(() => false, 300);
                Shot(window, folder, "editor-zoom-chosen.png");
            }
            finally
            {
                while (window.Timeline?.CanUndo == true)
                {
                    window.Timeline.Undo(); // with changes, closing would stop to ask whether to discard them
                }
                window.Close();
            }
        });
    }

    private static void Shot(Window window, string? folder, string name)
    {
        window.UpdateLayout();
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var visual = (System.Windows.Controls.Grid)window.Content;
        visual.Background = window.Background;
        var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using FileStream stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }
}
