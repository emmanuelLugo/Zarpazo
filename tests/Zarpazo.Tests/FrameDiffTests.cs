using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class FrameDiffTests
{
    private static byte[] Frame(int width, int height, byte fill = 10) => Enumerable.Repeat(fill, width * height * 4).ToArray();

    private static void Paint(byte[] frame, int width, int x, int y, byte value) => frame.AsSpan((y * width + x) * 4, 4).Fill(value);

    [Fact]
    public void Identical_frames_have_no_changes()
    {
        Assert.Null(FrameDiff.ChangedRect(Frame(8, 6), Frame(8, 6), 8, 6));
    }

    [Fact]
    public void One_changed_pixel_gives_a_one_pixel_rectangle()
    {
        byte[] now = Frame(8, 6);
        Paint(now, 8, 5, 2, 99);

        Assert.Equal((5, 2, 1, 1), FrameDiff.ChangedRect(Frame(8, 6), now, 8, 6));
    }

    [Fact]
    public void The_rectangle_covers_every_change()
    {
        byte[] now = Frame(10, 10);
        Paint(now, 10, 2, 7, 50);  // bottom-left of the change
        Paint(now, 10, 8, 3, 60);  // top-right of the change

        Assert.Equal((2, 3, 7, 5), FrameDiff.ChangedRect(Frame(10, 10), now, 10, 10));
    }

    [Fact]
    public void A_change_in_the_first_and_last_column_and_row_is_found()
    {
        byte[] now = Frame(5, 4);
        Paint(now, 5, 0, 0, 1);
        Paint(now, 5, 4, 3, 2);

        Assert.Equal((0, 0, 5, 4), FrameDiff.ChangedRect(Frame(5, 4), now, 5, 4));
    }

    [Fact]
    public void Crop_copies_only_the_rectangle()
    {
        byte[] frame = Frame(6, 4);
        Paint(frame, 6, 2, 1, 77);
        Paint(frame, 6, 3, 2, 88);

        byte[] crop = FrameDiff.Crop(frame, 6, 2, 1, 2, 2);

        Assert.Equal(2 * 2 * 4, crop.Length);
        Assert.Equal(77, crop[0]);            // (2, 1) is the first pixel
        Assert.Equal(88, crop[(1 * 2 + 1) * 4]); // (3, 2) is the last
        Assert.Equal(10, crop[4]);            // (3, 1) was never painted
    }
}
