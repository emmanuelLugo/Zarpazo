using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class FrameLayoutExportTests
{
    private static readonly VideoFrameStyle Framed = VideoFrameStyle.Default with { Enabled = true };

    [Theory]
    [InlineData(1266, 792)]
    [InlineData(1920, 1080)]
    [InlineData(1202, 724)]
    [InlineData(801, 601)]
    public void The_scene_grows_so_the_recording_keeps_its_own_pixels(int width, int height)
    {
        FrameLayout layout = FrameLayout.ForExport(Framed, width, height, width, height);

        Assert.Equal(width, layout.Content.Width);
        Assert.Equal(height, layout.Content.Height);
        Assert.Equal(1, layout.ContentScale);
        Assert.True(layout.Width > width && layout.Height > height);
        Assert.Equal(0, layout.Width % 2);
        Assert.Equal(0, layout.Height % 2);
        Assert.InRange(layout.Width / (double)layout.Height, width / (double)height * 0.99, width / (double)height * 1.01);
    }

    [Fact]
    public void A_tall_canvas_grows_around_a_wide_recording_too()
    {
        VideoFrameStyle style = Framed.ForScene(letterboxed: true);
        (int width, int height) = VideoAspect.Tall.SceneSize(1266, 792);

        FrameLayout layout = FrameLayout.ForExport(style, width, height, 1266, 792);

        Assert.Equal(1266, layout.Content.Width);
        Assert.InRange(layout.Width / (double)layout.Height, 9 / 16.0 * 0.99, 9 / 16.0 * 1.01);
    }

    [Fact]
    public void It_grows_no_bigger_than_4k()
    {
        FrameLayout layout = FrameLayout.ForExport(Framed, 3840, 2160, 3840, 2160);

        Assert.True(layout.Width <= 3840 && layout.Height <= 2160);
    }
}
