using System.Windows.Input;
using Zarpazo.Shell;

namespace Zarpazo.Tests;

public class HotKeyBindingTests
{
    [Fact]
    public void The_default_is_print_screen_alone()
    {
        Assert.True(HotKeyBinding.Default.IsAcceptable);
        Assert.Equal("Impr Pant", HotKeyBinding.Default.Describe());
    }

    [Theory]
    [InlineData(Key.PrintScreen, ModifierKeys.None, true)]
    [InlineData(Key.F9, ModifierKeys.None, true)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [InlineData(Key.D4, ModifierKeys.Windows | ModifierKeys.Shift, true)]
    [InlineData(Key.A, ModifierKeys.None, false)]   // would be taken from every program's typing
    [InlineData(Key.A, ModifierKeys.Shift, false)]  // so would capital letters
    [InlineData(Key.Space, ModifierKeys.None, false)]
    public void Only_combinations_that_do_not_break_typing_are_accepted(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, HotKeyBinding.From(key, modifiers).IsAcceptable);
    }

    [Fact]
    public void An_empty_binding_is_not_acceptable()
    {
        Assert.False(new HotKeyBinding(0, 0).IsAcceptable);
    }

    [Theory]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Mayús+S")]
    [InlineData(Key.D4, ModifierKeys.Windows | ModifierKeys.Alt, "Alt+Win+4")]
    [InlineData(Key.F9, ModifierKeys.None, "F9")]
    public void Describes_the_combination_in_spanish_with_a_fixed_order(Key key, ModifierKeys modifiers, string expected)
    {
        Assert.Equal(expected, HotKeyBinding.From(key, modifiers).Describe());
    }
}
