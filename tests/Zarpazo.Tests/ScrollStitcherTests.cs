using Zarpazo.Capture;

namespace Zarpazo.Tests;

public sealed class ScrollStitcherTests
{
    private const int Width = 40;
    private const int Height = 100;

    // A tall "page": every row different, like text, so each shift has one right answer.
    private static byte[] Page(int rows, int seed = 1)
    {
        var random = new Random(seed);
        var page = new byte[rows * Width * 4];
        random.NextBytes(page);
        for (int i = 3; i < page.Length; i += 4)
        {
            page[i] = 255;
        }
        return page;
    }

    private static byte[] Window(byte[] page, int top)
    {
        var frame = new byte[Height * Width * 4];
        Buffer.BlockCopy(page, top * Width * 4, frame, 0, frame.Length);
        return frame;
    }

    private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private static byte[] Rows(byte[] page, int count)
    {
        var rows = new byte[count * Width * 4];
        Buffer.BlockCopy(page, 0, rows, 0, rows.Length);
        return rows;
    }

    [Fact]
    public void Joins_pictures_scrolled_by_the_same_amount_into_the_original()
    {
        byte[] page = Page(400);
        var stitcher = new ScrollStitcher(Width, Height);
        for (int top = 0; top <= 300; top += 50)
        {
            Assert.Equal(ScrollStep.Added, stitcher.Add(Window(page, top)));
        }

        Assert.Equal(400, stitcher.TotalHeight);
        Assert.Equal(Rows(page, 400), Pixels(stitcher.Result()!));
    }

    [Fact]
    public void Joins_pictures_scrolled_by_different_amounts()
    {
        byte[] page = Page(400);
        var stitcher = new ScrollStitcher(Width, Height);
        foreach (int top in new[] { 0, 70, 130, 180, 250 })
        {
            Assert.Equal(ScrollStep.Added, stitcher.Add(Window(page, top)));
        }

        Assert.Equal(350, stitcher.TotalHeight);
        Assert.Equal(Rows(page, 350), Pixels(stitcher.Result()!));
    }

    [Fact]
    public void Reports_the_end_when_the_picture_does_not_change()
    {
        byte[] page = Page(300);
        var stitcher = new ScrollStitcher(Width, Height);
        stitcher.Add(Window(page, 0));
        stitcher.Add(Window(page, 60));

        Assert.Equal(ScrollStep.End, stitcher.Add(Window(page, 60)));
        Assert.Equal(160, stitcher.TotalHeight);
    }

    [Fact]
    public void Reports_lost_when_the_new_picture_has_nothing_in_common()
    {
        var stitcher = new ScrollStitcher(Width, Height);
        stitcher.Add(Window(Page(300, seed: 1), 0));

        Assert.Equal(ScrollStep.Lost, stitcher.Add(Window(Page(300, seed: 2), 0)));
        Assert.Equal(Height, stitcher.TotalHeight);
    }

    [Fact]
    public void Flat_rows_do_not_fool_the_alignment()
    {
        // Every other row is a plain colour, as between lines of text on a white page.
        byte[] page = Page(400);
        for (int row = 0; row < 400; row += 2)
        {
            for (int i = 0; i < Width * 4; i++)
            {
                page[row * Width * 4 + i] = 255;
            }
        }

        var stitcher = new ScrollStitcher(Width, Height);
        foreach (int top in new[] { 0, 33, 66, 99 })
        {
            Assert.Equal(ScrollStep.Added, stitcher.Add(Window(page, top)));
        }

        Assert.Equal(199, stitcher.TotalHeight);
        Assert.Equal(Rows(page, 199), Pixels(stitcher.Result()!));
    }

    [Fact]
    public void A_few_changed_rows_do_not_break_the_alignment()
    {
        byte[] page = Page(300);
        var stitcher = new ScrollStitcher(Width, Height);
        stitcher.Add(Window(page, 0));

        byte[] second = Window(page, 40);
        for (int i = 0; i < Width * 4 * 8; i++)
        {
            second[i] ^= 0x55; // a sticky header of 8 rows that is not the page's
        }

        Assert.Equal(ScrollStep.Added, stitcher.Add(second));
        Assert.Equal(140, stitcher.TotalHeight);
    }

    [Fact]
    public void Refuses_a_scroll_that_leaves_almost_nothing_in_common()
    {
        // 90 of 100 rows moved: less than a quarter of the picture is shared, which is too little to trust.
        byte[] page = Page(300);
        var stitcher = new ScrollStitcher(Width, Height);
        stitcher.Add(Window(page, 0));

        Assert.Equal(ScrollStep.Lost, stitcher.Add(Window(page, 90)));
    }
}
