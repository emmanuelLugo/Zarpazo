using System.Windows.Media;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

/// <summary>The shadow's own settings (softness, distance, colour) and the room of each side of the recording.</summary>
public sealed class FrameShadowAndSidesTests
{
    private const int Width = 1280, Height = 720;

    private static readonly VideoFrameStyle Framed = VideoFrameStyle.Default with { Enabled = true };

    [Fact]
    public void The_shadow_looks_as_it_always_did_until_it_is_changed()
    {
        System.Windows.Media.Effects.DropShadowEffect effect = FrameBackdrop.Shadow(Framed, FrameLayout.For(Framed, Width, Height), 1);

        Assert.Equal(Height * 0.035, effect.BlurRadius, 6);
        Assert.Equal(Height * 0.035 * 0.3, effect.ShadowDepth, 6);
        Assert.Equal(Colors.Black, effect.Color);
        Assert.Equal(0.8 * Framed.Shadow, effect.Opacity, 6);
    }

    [Fact]
    public void The_shadows_softness_distance_and_colour_are_the_ones_chosen()
    {
        VideoFrameStyle style = Framed with { ShadowBlur = 0.06, ShadowOffset = 0.02, ShadowColor = "#3366FF" };

        System.Windows.Media.Effects.DropShadowEffect effect = FrameBackdrop.Shadow(style, FrameLayout.For(style, Width, Height), 0.5);

        Assert.Equal(Height * 0.06 * 0.5, effect.BlurRadius, 6);
        Assert.Equal(Height * 0.02 * 0.5, effect.ShadowDepth, 6);
        Assert.Equal(Color.FromRgb(0x33, 0x66, 0xFF), effect.Color);
    }

    [Fact]
    public void A_coloured_shadow_tints_the_backdrop_below_the_recording()
    {
        VideoFrameStyle style = Framed with { Background = "blanco", Shadow = 1, ShadowBlur = 0.03, ShadowOffset = 0.02, ShadowColor = "#FF0000" };
        FrameLayout layout = FrameLayout.For(style, 640, 360);

        byte[] backdrop = FrameBackdrop.Render(style, layout);

        int x = 320, y = layout.Content.Y + layout.Content.Height + 4;
        int at = (y * 640 + x) * 4;
        Assert.True(backdrop[at + 2] > backdrop[at + 1] + 30, $"under the recording the white turns red (B {backdrop[at]}, G {backdrop[at + 1]}, R {backdrop[at + 2]})");
    }

    [Fact]
    public void Room_all_round_is_the_same_as_before_and_a_room_for_each_side_is_kept_apart()
    {
        Assert.Equal((0.07, 0.07, 0.07, 0.07), Framed.Room);
        Assert.Equal((0.01, 0.02, 0.03, 0.04), (Framed with { PaddingBySide = true, PaddingLeft = 0.01, PaddingTop = 0.02, PaddingRight = 0.03, PaddingBottom = 0.04 }).Room);
    }

    [Fact]
    public void More_room_below_pushes_the_recording_up_and_leaves_the_space_for_subtitles()
    {
        VideoFrameStyle style = Framed with { PaddingBySide = true, PaddingTop = 0.05, PaddingLeft = 0.05, PaddingRight = 0.05, PaddingBottom = 0.2 };

        FrameLayout layout = FrameLayout.For(style, Width, Height);

        Assert.True(layout.Content.Y >= (int)(0.05 * Height) - 1, "no closer to the top than asked");
        Assert.True(layout.Content.Y + layout.Content.Height <= Height - (int)(0.2 * Height) + 1, "no closer to the bottom than asked");
        Assert.True(layout.Content.X >= (int)(0.05 * Height) - 1 && layout.Content.X + layout.Content.Width <= Width - (int)(0.05 * Height) + 1);
        Assert.Equal(Width / (double)Height, (double)layout.Content.Width / layout.Content.Height, 1);
        Assert.True(layout.Content.Y + layout.Content.Height / 2 < Height / 2, "the recording sits above the middle");
    }

    [Fact]
    public void The_recording_is_centred_in_the_space_the_room_leaves_on_each_side()
    {
        VideoFrameStyle style = Framed with { PaddingBySide = true, PaddingLeft = 0.3, PaddingRight = 0.02, PaddingTop = 0.07, PaddingBottom = 0.07 };

        FrameLayout layout = FrameLayout.For(style, Width, Height);

        int left = layout.Content.X - (int)Math.Round(0.3 * Height);
        int right = Width - (int)Math.Round(0.02 * Height) - (layout.Content.X + layout.Content.Width);
        Assert.InRange(left - right, -1, 1);
    }

    [Fact]
    public void Even_the_most_room_possible_leaves_a_recording_to_see_in_a_tall_video()
    {
        VideoFrameStyle style = Framed with { PaddingBySide = true, PaddingTop = 0.3, PaddingBottom = 0.3, PaddingLeft = 0.3, PaddingRight = 0.3, Bar = WindowBar.Mac };

        FrameLayout layout = FrameLayout.For(style, 360, 640);

        Assert.True(layout.Content.Width >= 36 && layout.Content.Height >= 20, $"{layout.Content.Width}x{layout.Content.Height}");
        Assert.True(layout.Content.X >= 0 && layout.Content.X + layout.Content.Width <= 360);
        Assert.True(layout.Card.Y >= 0 && layout.Card.Y + layout.Card.Height <= 640);
    }

    [Fact]
    public void A_video_in_another_shape_has_the_background_with_no_room_for_sides_either()
    {
        VideoFrameStyle off = (Framed with { Enabled = false, PaddingBySide = true, PaddingBottom = 0.25 }).ForScene(letterboxed: true);

        Assert.Equal((0.0, 0.0, 0.0, 0.0), off.Room);
    }

    [Fact]
    public void Odd_values_in_a_file_are_brought_back_inside_what_is_offered()
    {
        VideoFrameStyle odd = (Framed with
        {
            ShadowBlur = 9, ShadowOffset = -1, ShadowColor = "negro", PaddingTop = 5, PaddingBottom = double.NaN, PaddingLeft = -2,
        }).Tidied();

        Assert.Equal(VideoFrameStyle.MaxShadowBlur, odd.ShadowBlur);
        Assert.Equal(0, odd.ShadowOffset);
        Assert.Equal("#000000", odd.ShadowColor);
        Assert.Equal(VideoFrameStyle.MaxSidePadding, odd.PaddingTop);
        Assert.Equal(VideoFrameStyle.DefaultSidePadding, odd.PaddingBottom);
        Assert.Equal(0, odd.PaddingLeft);
    }

    [Fact]
    public void The_settings_file_keeps_the_shadow_and_the_rooms_and_an_old_file_gets_the_defaults()
    {
        var settings = new Zarpazo.Settings.AppSettings
        {
            VideoFrame = Framed with { ShadowBlur = 0.05, ShadowOffset = 0.03, ShadowColor = "#102030", PaddingBySide = true, PaddingTop = 0.01, PaddingBottom = 0.25, PaddingLeft = 0.1, PaddingRight = 0.12 },
        };

        var back = System.Text.Json.JsonSerializer.Deserialize<Zarpazo.Settings.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;

        VideoFrameStyle frame = back.VideoFrame;
        Assert.Equal((0.05, 0.03, "#102030", true), (frame.ShadowBlur, frame.ShadowOffset, frame.ShadowColor, frame.PaddingBySide));
        Assert.Equal((0.01, 0.25, 0.1, 0.12), (frame.PaddingTop, frame.PaddingBottom, frame.PaddingLeft, frame.PaddingRight));

        var old = System.Text.Json.JsonSerializer.Deserialize<Zarpazo.Settings.AppSettings>("""{ "VideoFrameEnabled": true, "VideoFramePadding": 0.1 }""")!;
        Assert.Equal((VideoFrameStyle.DefaultShadowBlur, VideoFrameStyle.DefaultShadowOffset, false), (old.VideoFrame.ShadowBlur, old.VideoFrame.ShadowOffset, old.VideoFrame.PaddingBySide));
    }
}
