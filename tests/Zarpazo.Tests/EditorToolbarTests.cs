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
}
