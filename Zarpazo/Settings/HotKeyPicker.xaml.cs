using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zarpazo.Shell;

namespace Zarpazo.Settings;

/// <summary>A box that takes the key combination the person presses, with a button to go back to the default.</summary>
public partial class HotKeyPicker : UserControl
{
    private const string DefaultHint = "Haz clic en el cuadro y pulsa la combinación que quieras.";

    private HotKeyBinding _value;

    public HotKeyPicker()
    {
        InitializeComponent();
        Hint.Text = DefaultHint;
    }

    internal event Action? ValueChanged;

    /// <summary>What "Restablecer" goes back to.</summary>
    internal HotKeyBinding DefaultValue { get; set; }

    internal HotKeyBinding Value
    {
        get => _value;
        set
        {
            _value = value;
            Box.Text = value.Describe();
            ValueChanged?.Invoke();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Capture(e.Key == Key.System ? e.SystemKey : e.Key);
    }

    // Windows reports Print Screen only when the key is released.
    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Snapshot)
        {
            e.Handled = true;
            Capture(Key.Snapshot);
        }
    }

    private void Capture(Key key)
    {
        if (key is Key.None or Key.DeadCharProcessed or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // a modifier on its own isn't a shortcut yet
        }

        HotKeyBinding binding = HotKeyBinding.From(key, Keyboard.Modifiers);
        if (!binding.IsAcceptable)
        {
            Hint.Text = "Usa Ctrl, Alt o Win junto con la tecla. Solo Impr Pant y F1 a F24 pueden ir solas.";
            return;
        }

        Value = binding;
        Hint.Text = DefaultHint;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        Value = DefaultValue;
        Hint.Text = DefaultHint;
    }
}
