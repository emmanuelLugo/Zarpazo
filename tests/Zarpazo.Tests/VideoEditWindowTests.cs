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

    // Clicks a button from the window's own message loop, as a person would: its awaits then come back to the window's thread.
    private static void ClickLater(System.Windows.Controls.Button button) =>
        button.Dispatcher.BeginInvoke(() => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));

    [Fact]
    public void The_window_can_be_created_without_failing_while_its_controls_load()
    {
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(Path.Combine(_folder, "not-there.mp4")); // opening the file fails later, asynchronously

            Assert.False(window.SaveButton.IsEnabled); // nothing can be saved before the video has opened
            Assert.False(window.CopyButton.IsEnabled);
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
                Assert.True(window.SaveButton.IsEnabled, "the recording can be saved as it is");
                Assert.False(window.NeedsRendering, "nothing to make again before an edit");

                timeline.Split(1);
                timeline.Split(2);
                Pump(() => false, 100);
                Assert.True(window.NeedsRendering);
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
                Assert.False(window.NeedsRendering, "back to the whole video, nothing to make again");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void An_edited_video_is_saved_where_asked_without_touching_the_recording()
    {
        string copy = Path.Combine(_folder, "recording.mp4");
        string chosen = Path.Combine(_folder, "elegido.mp4");
        File.Copy(_samples.Silent, copy);

        Sta.Run(() =>
        {
            var window = new VideoEditWindow(copy) { AskSavePath = () => chosen };
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                window.Timeline!.Split(1);
                window.Timeline.Delete(0);
                ClickLater(window.SaveButton);

                Assert.True(Pump(() => File.Exists(chosen) && window.SaveButton.IsEnabled, 40000), "the video should be saved");
                Assert.True(File.Exists(copy), "the recording is left as it was");
                Assert.Equal(Path.Combine(_folder, "recording_editado.mp4"), window.Delivery.ReadyPath);
                Assert.Equal(Visibility.Visible, window.ShowInFolderButton.Visibility);
                Assert.Contains("Guardado", window.DeliveryText.Text);
            }
            finally
            {
                window.Close(); // saved, so closing asks nothing
            }
        });
    }

    [Fact]
    public void Copying_a_recording_with_nothing_to_change_hands_it_out_as_it_is()
    {
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(_samples.Silent, delivery: new VideoDelivery(_samples.Silent, Path.Combine(_folder, "listo.mp4")));
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                ClickLater(window.CopyButton);

                Assert.True(Pump(() => window.Delivery.Delivered, 10000), "the video should be ready");
                Assert.Equal(new FileInfo(_samples.Silent).Length, new FileInfo(Path.Combine(_folder, "listo.mp4")).Length);
            }
            finally
            {
                window.Close();
            }
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
                Assert.True(window.NeedsRendering, "the zooms alone make a different video");
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

    [NeedsGpuFact]
    public void Turning_the_frame_on_puts_the_recording_on_a_background_and_makes_it_worth_saving()
    {
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(_samples.WithSound);
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zarpazo;component/Theme/Dark.xaml", UriKind.Relative) });
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                Assert.False(window.NeedsRendering,
                    $"edited {window.Timeline!.IsEdited}, zooms {window.Timeline.Zooms.Count}, frame {window.FrameBox.IsChecked}, track {window.Track != null}");
                Assert.Equal(8, window.FrameBackgrounds.Children.Count);

                window.FrameBox.IsChecked = true;
                window.FrameBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => false, 300);

                Assert.Equal(Visibility.Visible, window.Backdrop.Visibility);
                Assert.True(window.ContentHost.Width < window.Stage.Width, "the recording is smaller, inside its frame");
                Assert.True(window.NeedsRendering, "a framed video is a different video");
                Assert.False(window.Timeline!.HasChanges, "the frame is a style, not an edit to discard");
                Shot(window, folder, "editor-frame.png");
            }
            finally
            {
                window.Close();
            }
        });
    }

    // A copy of the sample with a pointer track: recorded without the pointer (as videos are now), or with it painted in.
    private string WithTrack(bool pointerInPicture)
    {
        string video = Path.Combine(_folder, pointerInPicture ? "old.mp4" : "clean.mp4");
        File.Copy(_samples.Silent, video);
        var moves = Enumerable.Range(0, 90).Select(i => new CursorSample(i * 33, 40 + i * 2, 60 + i)).ToList();
        new CursorTrack(320, 180, moves, [], pointerInPicture).Save(video);
        return video;
    }

    [NeedsGpuFact]
    public void A_video_recorded_without_the_pointer_shows_the_one_that_will_be_drawn_and_a_new_style_is_worth_saving()
    {
        string? folder = Environment.GetEnvironmentVariable("ZARPAZO_SCREENSHOTS");
        string video = WithTrack(pointerInPicture: false);
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(video);
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zarpazo;component/Theme/Dark.xaml", UriKind.Relative) });
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                Assert.True(window.CursorBox.IsEnabled);
                Assert.Equal(Visibility.Visible, window.PointerLayer.Visibility);
                Assert.Equal(Visibility.Visible, window.PointerGlyph.Visibility);
                Assert.Equal(3, window.CursorShapes.Children.Count);
                Assert.True(window.NeedsRendering, "the pointer has to be drawn in");

                window.CursorTab.IsChecked = true;
                Assert.Equal(Visibility.Visible, window.CursorPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.FramePanel.Visibility);
                window.CursorSizeSlider.Value = 3;
                Pump(() => false, 200);

                Assert.Equal(3, window.PointerStyle.Size);
                Assert.False(window.Timeline!.HasChanges, "the pointer is a style, not an edit to discard");
                Shot(window, folder, "editor-cursor.png");

                window.CursorBox.IsChecked = false;
                window.CursorBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(Visibility.Collapsed, window.PointerLayer.Visibility);
                Assert.False(window.CursorOptions.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [NeedsGpuFact]
    public void A_video_with_the_pointer_painted_in_offers_no_other_and_says_why()
    {
        string video = WithTrack(pointerInPicture: true);
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(video);
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                Assert.False(window.CursorBox.IsEnabled);
                Assert.Equal(Visibility.Visible, window.CursorNote.Visibility);
                Assert.Equal(Visibility.Collapsed, window.PointerLayer.Visibility);
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
