using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class EditorToolbarTests
{
    private static IEnumerable<RadioButton> RadioButtons(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is RadioButton radio)
            {
                yield return radio;
            }
            if (child is DependencyObject node)
            {
                foreach (RadioButton nested in RadioButtons(node))
                {
                    yield return nested;
                }
            }
        }
    }

    private static RadioButton Find(EditorToolbar toolbar, string group, string tag) =>
        RadioButtons(toolbar).First(r => r.GroupName == group && (string)r.Tag == tag);

    // What the mouse does first when it presses a button.
    private static void Press(RadioButton radio) => radio.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
    {
        RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
        Source = radio,
    });

    [Fact]
    public void Starts_adjusting_the_area()
    {
        Sta.Run(() => Assert.Equal(AnnotationTool.Select, new EditorToolbar().Tool));
    }

    [Fact]
    public void Choosing_a_tool_selects_it()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            RadioButton rectangle = Find(toolbar, "tool", "Rectangle");

            rectangle.IsChecked = true;

            Assert.Equal(AnnotationTool.Rectangle, toolbar.Tool);
        });
    }

    [Fact]
    public void Pressing_the_active_tool_again_goes_back_to_adjusting_the_area()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            RadioButton ellipse = Find(toolbar, "tool", "Ellipse");
            ellipse.IsChecked = true;

            Press(ellipse);

            Assert.Equal(AnnotationTool.Select, toolbar.Tool);
            Assert.True(Find(toolbar, "tool", "Select").IsChecked);
            Assert.False(ellipse.IsChecked);
        });
    }

    [Fact]
    public void Pressing_a_different_tool_does_not_go_back()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            Find(toolbar, "tool", "Ellipse").IsChecked = true;
            RadioButton arrow = Find(toolbar, "tool", "Arrow");

            Press(arrow); // pressing an inactive tool must leave the normal click to check it
            arrow.IsChecked = true; // which the real mouse does right after

            Assert.Equal(AnnotationTool.Arrow, toolbar.Tool);
        });
    }

    [Fact]
    public void Pressing_the_adjust_tool_while_it_is_active_changes_nothing()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Press(Find(toolbar, "tool", "Select"));

            Assert.Equal(AnnotationTool.Select, toolbar.Tool);
        });
    }

    [Fact]
    public void Restore_picks_a_listed_color_and_thickness_and_checks_their_buttons()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            toolbar.Restore("#1e88e5", 8); // lower case, as a hand-edited file might have it

            Assert.Equal(System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5), toolbar.Color);
            Assert.Equal(8, toolbar.Thickness);
            Assert.True(Find(toolbar, "color", "#1E88E5").IsChecked);
            Assert.True(Find(toolbar, "size", "8").IsChecked);
            Assert.Equal("#1E88E5", toolbar.ColorHex);
        });
    }

    [Fact]
    public void Restore_shows_an_unlisted_color_as_the_custom_choice()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            toolbar.Restore("#123456", 2);

            Assert.Equal("#123456", toolbar.ColorHex);
            Assert.True(Find(toolbar, "color", "#123456").IsChecked);
        });
    }

    [Fact]
    public void Restore_ignores_values_it_cannot_understand()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            toolbar.Restore("not a color", 5);

            Assert.Equal("#E53935", toolbar.ColorHex);
            Assert.Equal(4, toolbar.Thickness);
        });
    }

    [Fact]
    public void Pressing_the_checked_color_keeps_the_tool_and_the_color()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            Find(toolbar, "tool", "Pen").IsChecked = true;
            RadioButton red = Find(toolbar, "color", "#E53935");
            Assert.True(red.IsChecked);

            Press(red);

            Assert.Equal(AnnotationTool.Pen, toolbar.Tool);
            Assert.True(red.IsChecked);
        });
    }

    [Fact]
    public void The_beautify_panel_offers_every_background_and_starts_off_with_the_default_style()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Assert.Equal(Zarpazo.Output.BeautifyBackground.All.Count, RadioButtons(toolbar).Count(r => r.GroupName == "beautyBackground"));
            Assert.False(toolbar.BeautifyEnabled);
            Assert.Equal(Zarpazo.Output.BeautifyStyle.Default, toolbar.BeautifyStyle);
            Assert.True(Find(toolbar, "beautyBackground", "violeta").IsChecked);
        });
    }

    [Fact]
    public void Restoring_the_beautify_choices_checks_their_buttons_without_changing_them()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            var style = new Zarpazo.Output.BeautifyStyle("bosque", 80, 0, Shadow: false);

            toolbar.RestoreBeautify(enabled: true, style);

            Assert.True(toolbar.BeautifyEnabled);
            Assert.Equal(style, toolbar.BeautifyStyle); // the radio buttons firing Checked must not overwrite it
            Assert.True(Find(toolbar, "beautyBackground", "bosque").IsChecked);
            Assert.True(Find(toolbar, "beautyPadding", "80").IsChecked);
            Assert.True(Find(toolbar, "beautyRadius", "0").IsChecked);
        });
    }

    [Fact]
    public void Choosing_an_option_in_the_panel_changes_the_style()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Find(toolbar, "beautyBackground", "oceano").IsChecked = true;
            Find(toolbar, "beautyPadding", "24").IsChecked = true;

            Assert.Equal("oceano", toolbar.BeautifyStyle.Background);
            Assert.Equal(24, toolbar.BeautifyStyle.Padding);
            Assert.Equal(12, toolbar.BeautifyStyle.Radius); // untouched
        });
    }

    [Fact]
    public void The_recording_panel_starts_as_a_gif_and_the_sound_boxes_wait_for_a_video()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Assert.Equal(Zarpazo.Recording.RecordingOptions.Default, toolbar.RecordingOptions);
            Assert.False(toolbar.RecordMicrophoneBox.IsEnabled);
            Assert.False(toolbar.RecordSystemSoundBox.IsEnabled);
            Assert.False(toolbar.RecordMicrophoneList.IsEnabled);
        });
    }

    [Fact]
    public void Choosing_video_turns_the_sound_boxes_on_and_the_choices_are_kept()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Find(toolbar, "recordFormat", "video").IsChecked = true;
            toolbar.RecordSystemSoundBox.IsChecked = true;
            toolbar.RecordSystemSoundBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.True(toolbar.RecordMicrophoneBox.IsEnabled);
            Assert.Equal(new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, false, true), toolbar.RecordingOptions);
        });
    }

    [Fact]
    public void Restoring_the_recording_choices_shows_them_without_changing_them()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            var saved = new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, true, true, "some-device-id");

            toolbar.RestoreRecording(saved);

            Assert.Equal(saved, toolbar.RecordingOptions); // including a microphone that is not connected right now
            Assert.True(Find(toolbar, "recordFormat", "video").IsChecked);
            Assert.True(toolbar.RecordMicrophoneBox.IsChecked);
            Assert.True(toolbar.RecordSystemSoundBox.IsChecked);
        });
    }

    [Fact]
    public void Restoring_a_chosen_output_device_keeps_it_and_the_list_waits_for_a_video()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            Assert.False(toolbar.RecordSpeakersList.IsEnabled);

            var saved = new Zarpazo.Recording.RecordingOptions(Zarpazo.Recording.RecordingFormat.Video, false, true, "", "some-speakers");
            toolbar.RestoreRecording(saved);

            Assert.True(toolbar.RecordSpeakersList.IsEnabled);
            Assert.Equal(saved, toolbar.RecordingOptions);
        });
    }

    // The button that stands for a group of tools, found by its name.
    private static Button Group(EditorToolbar toolbar, string title) => Buttons(toolbar).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == title);

    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is Button button)
            {
                yield return button;
            }
            if (child is DependencyObject node)
            {
                foreach (Button nested in Buttons(node))
                {
                    yield return nested;
                }
            }
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    [Fact]
    public void Clicking_a_group_uses_the_tool_it_remembers_and_clicking_again_lets_go()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Click(Group(toolbar, "Formas"));
            Assert.Equal(AnnotationTool.Arrow, toolbar.Tool); // the arrow is what the shapes start with

            Click(Group(toolbar, "Formas"));
            Assert.Equal(AnnotationTool.Select, toolbar.Tool);
        });
    }

    [Fact]
    public void A_group_remembers_the_last_tool_chosen_from_its_menu()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            Find(toolbar, "tool", "Ellipse").IsChecked = true; // picked from the Formas menu
            Find(toolbar, "tool", "Select").IsChecked = true;  // then back to adjusting

            Click(Group(toolbar, "Formas"));

            Assert.Equal(AnnotationTool.Ellipse, toolbar.Tool);
        });
    }

    [Fact]
    public void Only_one_tool_is_checked_even_when_the_buttons_live_in_different_menus()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Find(toolbar, "tool", "Pen").IsChecked = true;   // in the Dibujo menu
            Find(toolbar, "tool", "Ruler").IsChecked = true; // in the Medir menu
            Find(toolbar, "tool", "Text").IsChecked = true;  // in the bar

            Assert.Equal(1, RadioButtons(toolbar).Count(r => r.GroupName == "tool" && r.IsChecked == true));
            Assert.True(Find(toolbar, "tool", "Text").IsChecked);
        });
    }

    [Theory]
    [InlineData("Select", false)]
    [InlineData("Move", false)]
    [InlineData("Pixelate", false)]
    [InlineData("Ruler", false)]
    [InlineData("Picker", false)]
    [InlineData("Rectangle", true)]
    [InlineData("Arrow", true)]
    [InlineData("Highlighter", true)]
    [InlineData("Text", true)]
    [InlineData("Step", true)]
    public void Color_and_thickness_only_show_for_the_tools_that_use_them(string tool, bool shown)
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();

            Find(toolbar, "tool", tool).IsChecked = true;

            Assert.Equal(shown ? Visibility.Visible : Visibility.Collapsed, toolbar.StyleTools.Visibility);
        });
    }

    [Fact]
    public void The_tools_column_is_short_and_the_adjust_tool_has_no_button()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            toolbar.Place(new Rect(200, 100, 400, 300), new Size(1366, 768));

            Assert.Equal(Visibility.Collapsed, Find(toolbar, "tool", "Select").Visibility);
            Assert.True(toolbar.ToolsBar.DesiredSize.Height < 300, $"column is {toolbar.ToolsBar.DesiredSize.Height} px tall");

            Find(toolbar, "tool", "Arrow").IsChecked = true; // with color and thickness it grows, and the bars are placed again
            Assert.True(toolbar.ToolsBar.DesiredSize.Height < 400);
        });
    }

    [Fact]
    public void The_more_menu_shows_when_beautify_is_on()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            Assert.Equal(Visibility.Collapsed, toolbar.BeautifyDot.Visibility);

            toolbar.RestoreBeautify(enabled: true, Zarpazo.Output.BeautifyStyle.Default);

            Assert.Equal(Visibility.Visible, toolbar.BeautifyDot.Visibility); // on the button, so it is not forgotten
            Assert.Equal(Visibility.Visible, toolbar.BeautifyState.Visibility); // and spelled out in the menu
        });
    }

    [Fact]
    public void Releasing_the_button_over_an_entry_of_a_group_menu_picks_it()
    {
        Sta.Run(() =>
        {
            var toolbar = new EditorToolbar();
            RadioButton ellipse = Find(toolbar, "tool", "Ellipse");

            // The menu opened when the button went down on the corner; the release lands on the entry without a press on it.
            ellipse.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = ellipse,
            });

            Assert.Equal(AnnotationTool.Ellipse, toolbar.Tool);
            Assert.True(ellipse.IsChecked);
        });
    }
}

