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
    public void The_speed_button_shows_the_speed_of_the_chosen_piece()
    {
        Sta.Run(() =>
        {
            var window = new VideoEditWindow(_samples.Silent);
            window.Show();
            try
            {
                Assert.True(Pump(() => window.Timeline != null), "the video should open");
                EditTimeline timeline = window.Timeline!;
                Pump(() => window.SpeedButton.IsEnabled, 2000);
                Assert.Equal("1×", window.SpeedText.Text);

                timeline.SetSpeed(0, 2);
                Pump(() => false, 100);

                Assert.Equal("2×", window.SpeedText.Text);
                Assert.True(window.NeedsRendering, "a faster piece is made again when saved");
                Assert.InRange(timeline.Duration, 1.25, 1.75);
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
                Assert.True(Pump(() => window.Timeline != null), $"the video should open: {window.KeptText.Text}");
                Assert.False(window.NeedsRendering,
                    $"edited {window.Timeline!.IsEdited}, zooms {window.Timeline.Zooms.Count}, frame {window.FrameBox.IsChecked}, track {window.Track != null}, look {window.Look}, timeline {window.Timeline.Look}, undo {window.Timeline.CanUndo}");
                Assert.Equal(Zarpazo.Output.BeautifyBackground.All.Count, window.FrameBackgrounds.Children.Count);

                window.FrameBox.IsChecked = true;
                window.FrameBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => false, 300);

                Assert.Equal(Visibility.Visible, window.Backdrop.Visibility);
                Assert.True(window.ContentHost.RenderTransform.Value.M11 < 1, "the recording is smaller, inside its frame");
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
                Pump(() => false, 100); // drawn again with the next frame
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

    [NeedsGpuFact]
    public void The_crop_is_chosen_on_the_whole_picture_shown_as_the_result_elsewhere_and_undone_like_any_edit()
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
                Assert.True(Pump(() => window.Timeline != null), $"the video should open: {window.KeptText.Text}");
                EditTimeline timeline = window.Timeline!;

                window.CropTab.IsChecked = true;
                Pump(() => false, 100);
                Assert.Equal(Visibility.Visible, window.CropPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.CropLayer.Visibility);
                Assert.Equal(Visibility.Collapsed, window.PointerLayer.Visibility);

                // A drag along the "Inferior" row is one step to undo.
                timeline.BeginGesture();
                window.CropBottomSlider.Value = 0.1;
                window.CropBottomSlider.Value = 0.2;
                timeline.EndGesture();
                window.CropLeftSlider.Value = 0.25;
                Pump(() => false, 200);

                Assert.Equal(new VideoCrop(0.25, 0, 0, 0.2), timeline.Crop);
                Assert.Equal(320.0, window.Stage.Width); // chosen on the whole recording
                Assert.Equal("20 %", window.CropBottomText.Text.Replace(" ", " "));
                Assert.True(window.NeedsRendering, "a cropped video is a different video");
                Assert.True(timeline.HasChanges);
                Shot(window, folder, "editor-crop-choosing.png");

                window.FrameTab.IsChecked = true; // elsewhere, the result
                Pump(() => false, 200);
                CropArea area = CropArea.For(timeline.Crop, 320, 180);
                Assert.Equal(Visibility.Collapsed, window.CropLayer.Visibility);
                Assert.Equal((double)area.Width, window.Stage.Width);
                Assert.Equal((double)area.Height, window.Stage.Height);
                Assert.Equal(-area.Pixels.X, System.Windows.Controls.Canvas.GetLeft(window.Player));
                Assert.NotNull(window.ContentHost.Clip);
                Shot(window, folder, "editor-crop-result.png");

                window.UndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(new VideoCrop(0, 0, 0, 0.2), timeline.Crop);
                window.UndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => false, 200);
                Assert.Equal(VideoCrop.None, timeline.Crop);
                Assert.Equal(0.0, window.CropBottomSlider.Value);
                Assert.Equal(320.0, window.Stage.Width);
                Assert.Null(window.ContentHost.Clip);
            }
            finally
            {
                while (window.Timeline?.CanUndo == true)
                {
                    window.Timeline.Undo();
                }
                window.Close();
            }
        });
    }

    [NeedsGpuFact]
    public void A_preset_brings_back_frame_cursor_and_zoom_style_at_once_and_one_undo_takes_it_away()
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
                Assert.True(Pump(() => window.Timeline != null), $"the video should open: {window.KeptText.Text}");
                EditTimeline timeline = window.Timeline!;
                timeline.AddZoom(new ZoomRegion(0.5, 2, 1.5, 0.5, 0.5));

                // A tutorial look: framed, a big pointer, close and quick zooms.
                window.FrameBox.IsChecked = true;
                window.FrameBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.CursorSizeSlider.Value = 3;
                window.ZoomTab.IsChecked = true;
                window.ZoomStyleDepthSlider.Value = 2;
                window.ZoomFocusedTile.IsChecked = true;
                window.ZoomFocusedTile.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => false, 200);
                Assert.Equal(new ZoomStyle(2, ZoomMotion.Focused), window.Look.Zoom);
                Assert.Equal(2, timeline.Zooms[0].Scale); // the video's zooms follow the depth
                Shot(window, folder, "editor-zoom-style.png");

                Assert.True(window.SavePreset("Tutorial"));
                VideoLook tutorial = window.Look;
                Assert.False(window.SavePreset("   "));
                Assert.Single(window.Presets);

                // Back to the plain look, then the preset in one click.
                window.FrameBox.IsChecked = false;
                window.FrameBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.CursorSizeSlider.Value = 1.5;
                window.ZoomStyleDepthSlider.Value = 1.5;
                Pump(() => false, 200);
                VideoLook plain = window.Look;
                Assert.NotEqual(tutorial, plain);

                Assert.True(window.ApplyPreset("tutorial"));
                Pump(() => false, 200);
                Assert.Equal(tutorial, window.Look);
                Assert.Equal(Visibility.Visible, window.Backdrop.Visibility);
                Assert.Equal(3, window.CursorSizeSlider.Value); // the panels show it
                Assert.True(window.ZoomFocusedTile.IsChecked);
                Assert.Equal(2, timeline.Zooms[0].Scale); // the depth went with the preset

                window.UndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => false, 200);
                Assert.Equal(plain, window.Look); // all of it, in one step
                Assert.Equal(1.5, timeline.Zooms[0].Scale);
                Assert.Equal(1.5, window.CursorSizeSlider.Value);

                Assert.True(window.RenamePreset("Tutorial", "Clase"));
                Assert.False(window.RenamePreset("Clase", " "));
                window.PresetsPopup.IsOpen = true;
                Pump(() => false, 200);
                Assert.Single(window.PresetRows.Children);
                ShotOf((FrameworkElement)window.PresetsPopup.Child, folder, "editor-presets.png");
                window.PresetsPopup.IsOpen = false;
                Assert.True(window.DeletePreset("clase"));
                Assert.Empty(window.Presets);
                Assert.Equal(Visibility.Visible, window.PresetsEmptyText.Visibility);
            }
            finally
            {
                while (window.Timeline?.CanUndo == true)
                {
                    window.Timeline.Undo();
                }
                window.Close();
            }
        });
    }

    private static void ShotOf(FrameworkElement element, string? folder, string name)
    {
        element.UpdateLayout();
        if (string.IsNullOrEmpty(folder) || element.ActualWidth < 1)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var target = new RenderTargetBitmap((int)element.ActualWidth + 8, (int)element.ActualHeight + 8, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using FileStream stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
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
