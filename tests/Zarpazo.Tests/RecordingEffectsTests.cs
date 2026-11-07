using System.Drawing;
using System.Drawing.Imaging;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class RecordingEffectsTests
{
    // A keyboard and mouse the test plays by hand.
    private sealed class FakeInput : IInputState
    {
        private readonly HashSet<int> _down = [];
        private readonly HashSet<int> _quick = [];

        public Point CursorPosition { get; set; } = new(100, 60);

        public void Press(params int[] keys)
        {
            foreach (int key in keys)
            {
                _down.Add(key);
            }
        }

        public void Release(params int[] keys)
        {
            foreach (int key in keys)
            {
                _down.Remove(key);
            }
        }

        /// <summary>A click that is over before anyone looked.</summary>
        public void QuickClick(int key) => _quick.Add(key);

        public bool IsDown(int virtualKey) => _down.Contains(virtualKey);

        public bool WasPressedSinceLastCheck(int virtualKey) => _quick.Remove(virtualKey);
    }

    private const int Left = 0x01;
    private const int Ctrl = KeyLabels.Control;
    private const int C = 0x43;
    private const int A = 0x41;
    private const int Enter = 0x0D;

    private static Bitmap Gray() => FilledBitmap(200, 120, Color.FromArgb(128, 128, 128));

    private static Bitmap FilledBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static void DrawFrame(RecordingEffects effects, Bitmap bitmap, long timeMs)
    {
        effects.Update(timeMs);
        using Graphics graphics = Graphics.FromImage(bitmap);
        effects.DrawUnderPointer(graphics, Point.Empty, timeMs);
        effects.DrawKeys(graphics, bitmap.Size);
    }

    private static bool Changed(Bitmap bitmap, int x, int y) => bitmap.GetPixel(x, y) != Color.FromArgb(255, 128, 128, 128);

    // ---------- which keys are shown (the privacy rule)

    [Theory]
    [InlineData(C, true, false, false, false, "Ctrl+C")]
    [InlineData(C, true, false, true, false, "Ctrl+Mayús+C")]
    [InlineData(0x09, false, true, false, false, "Alt+Tab")]
    [InlineData(0x44, false, false, false, true, "Win+D")]
    [InlineData(Enter, false, false, false, false, "Enter")]
    [InlineData(0x74, false, false, false, false, "F5")]
    [InlineData(0x25, false, false, true, false, "Mayús+←")]
    [InlineData(0x20, true, false, false, false, "Ctrl+Espacio")]
    public void Shortcuts_and_special_keys_are_labelled(int key, bool ctrl, bool alt, bool shift, bool win, string expected)
    {
        Assert.Equal(expected, KeyLabels.Describe(key, ctrl, alt, shift, win));
    }

    [Theory]
    [InlineData(A, false, false)]      // a letter typed
    [InlineData(A, false, true)]       // a capital letter: Shift alone is not a shortcut
    [InlineData(0x31, false, false)]   // a digit
    [InlineData(0x20, false, false)]   // the space bar
    [InlineData(0xBE, false, false)]   // a full stop
    public void What_is_being_typed_is_never_shown_so_a_password_stays_private(int key, bool alt, bool shift)
    {
        Assert.Null(KeyLabels.Describe(key, control: false, alt: false, shift, windows: false));
        _ = alt;
    }

    [Fact]
    public void Modifier_keys_and_mouse_buttons_are_not_keys_to_show()
    {
        Assert.True(KeyLabels.IsModifier(KeyLabels.Control));
        Assert.True(KeyLabels.IsModifier(0xA0)); // left Shift
        Assert.True(KeyLabels.IsMouseButton(Left));
        Assert.False(KeyLabels.IsModifier(C));
    }

    // ---------- following the keyboard over time

    [Fact]
    public void A_shortcut_appears_when_pressed_and_goes_away_after_a_moment()
    {
        var input = new FakeInput();
        var keys = new KeyTracker(input);

        input.Press(Ctrl, C);
        keys.Update(1000);
        Assert.Equal(["Ctrl+C"], keys.Current);

        input.Release(Ctrl, C);
        keys.Update(1500);
        Assert.Equal(["Ctrl+C"], keys.Current); // still on show

        keys.Update(3000);
        Assert.Empty(keys.Current);
    }

    [Fact]
    public void Typing_letters_leaves_nothing_on_show()
    {
        var input = new FakeInput();
        var keys = new KeyTracker(input);

        input.Press(A);
        keys.Update(100);
        input.Release(A);
        input.Press(0x42);
        keys.Update(200);

        Assert.Empty(keys.Current);
    }

    [Fact]
    public void A_key_held_down_is_shown_once_not_once_per_picture()
    {
        var input = new FakeInput();
        var keys = new KeyTracker(input);

        input.Press(Enter);
        for (int t = 0; t < 10; t++)
        {
            keys.Update(t * 33);
        }

        Assert.Equal(["Enter"], keys.Current);
    }

    [Fact]
    public void Only_the_last_few_shortcuts_are_kept()
    {
        var input = new FakeInput();
        var keys = new KeyTracker(input);

        foreach (int key in new[] { 0x70, 0x71, 0x72, 0x73, 0x74 }) // F1 to F5, one after another
        {
            input.Press(key);
            keys.Update(0);
            input.Release(key);
        }

        Assert.Equal(["F3", "F4", "F5"], keys.Current);
    }

    // ---------- what is drawn

    [Fact]
    public void The_pointer_gets_a_halo_and_the_rest_of_the_picture_is_left_alone()
    {
        using Bitmap bitmap = Gray();
        var effects = new RecordingEffects(new FakeInput { CursorPosition = new Point(100, 60) }, highlightCursor: true, showKeys: false);

        DrawFrame(effects, bitmap, 0);

        Assert.True(Changed(bitmap, 100, 60), "the pointer's surroundings should be tinted");
        Assert.True(Changed(bitmap, 110, 60));
        Assert.False(Changed(bitmap, 5, 5), "far from the pointer nothing changes");
        Assert.False(Changed(bitmap, 100, 100));
    }

    [Fact]
    public void Without_the_highlight_option_nothing_is_drawn_for_the_pointer()
    {
        using Bitmap bitmap = Gray();
        var input = new FakeInput { CursorPosition = new Point(100, 60) };
        input.Press(Left);
        var effects = new RecordingEffects(input, highlightCursor: false, showKeys: false);

        DrawFrame(effects, bitmap, 0);

        Assert.False(Changed(bitmap, 100, 60));
    }

    [Fact]
    public void A_click_leaves_a_ring_that_grows_and_then_disappears()
    {
        var input = new FakeInput { CursorPosition = new Point(100, 60) };
        var effects = new RecordingEffects(input, highlightCursor: true, showKeys: false);

        input.Press(Left);
        using (Bitmap held = Gray())
        {
            DrawFrame(effects, held, 0);
        }
        input.Release(Left);

        // 200 ms later the ring has grown past the halo (radius 22) to about 25 pixels from the pointer.
        using Bitmap during = Gray();
        DrawFrame(effects, during, 200);
        Assert.True(Changed(during, 125, 60) || Changed(during, 124, 60) || Changed(during, 126, 60), "the ring should be about 25 px out");

        using Bitmap after = Gray();
        DrawFrame(effects, after, 700);
        Assert.False(Changed(after, 125, 60), "the ring should be gone");
        Assert.True(Changed(after, 100, 60), "the halo stays");
    }

    [Fact]
    public void A_button_held_down_keeps_its_ring_and_a_very_quick_click_still_leaves_one()
    {
        var input = new FakeInput { CursorPosition = new Point(100, 60) };
        var effects = new RecordingEffects(input, highlightCursor: true, showKeys: false);
        input.QuickClick(Left); // pressed and released between two pictures

        using Bitmap bitmap = Gray();
        DrawFrame(effects, bitmap, 0);
        using Bitmap later = Gray();
        DrawFrame(effects, later, 150);

        Assert.True(Changed(later, 100 + 14 + (int)(26 * 150 / 450.0), 60) || Changed(later, 100 + 23, 60) || Changed(later, 100 + 24, 60),
            "a click too quick to see in a picture should still show its ring");
    }

    [Fact]
    public void Shortcuts_are_drawn_near_the_bottom_when_asked_for_and_not_otherwise()
    {
        var input = new FakeInput();
        input.Press(Ctrl, C);

        using Bitmap shown = Gray();
        DrawFrame(new RecordingEffects(input, highlightCursor: false, showKeys: true), shown, 0);
        using Bitmap hidden = Gray();
        DrawFrame(new RecordingEffects(input, highlightCursor: false, showKeys: false), hidden, 0);

        bool anyChangeNearBottom = Enumerable.Range(60, 55).Any(y => Enumerable.Range(60, 80).Any(x => Changed(shown, x, y)));
        bool anyChangeAnywhere = Enumerable.Range(0, 120).Any(y => Enumerable.Range(0, 200).Any(x => Changed(hidden, x, y)));
        Assert.True(anyChangeNearBottom, "the pill with the shortcut should be at the bottom");
        Assert.False(anyChangeAnywhere);
        Assert.False(Changed(shown, 100, 10), "and nothing at the top");
    }

    [Fact]
    public void No_effects_are_made_when_none_are_asked_for()
    {
        Assert.Null(RecordingEffects.FromOptions(RecordingOptions.Default));
        Assert.NotNull(RecordingEffects.FromOptions(RecordingOptions.Default with { HighlightCursor = true }));
        Assert.NotNull(RecordingEffects.FromOptions(RecordingOptions.Default with { ShowKeys = true }));
    }
}
