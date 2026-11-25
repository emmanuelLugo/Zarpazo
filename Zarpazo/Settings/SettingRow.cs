using System.Windows;
using System.Windows.Controls;

namespace Zarpazo.Settings;

/// <summary>
/// One line of the settings window: a title and an optional explanation on the left, the control (its content) on the right.
/// With <see cref="Stacked"/> the control goes under the text, across the whole line, for a wide one such as a folder box.
/// The look is in SettingsWindow.xaml.
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty StackedProperty =
        DependencyProperty.Register(nameof(Stacked), typeof(bool), typeof(SettingRow));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool Stacked
    {
        get => (bool)GetValue(StackedProperty);
        set => SetValue(StackedProperty, value);
    }
}
