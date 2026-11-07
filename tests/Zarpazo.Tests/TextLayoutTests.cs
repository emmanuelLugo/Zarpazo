using Zarpazo.Ocr;

namespace Zarpazo.Tests;

public sealed class TextLayoutTests
{
    private const double Char = 10; // width of one character in these made-up boxes
    private const double Pitch = 30;

    private static OcrLineBox Line(string text, int column, int row) =>
        new(text, 20 + column * Char, 10 + row * Pitch, text.Length * Char, 20);

    [Fact]
    public void Keeps_the_indentation_of_each_line()
    {
        string text = TextLayout.Format([
            Line("{", 0, 0),
            Line("\"user\": {", 2, 1),
            Line("\"name\": \"Ana\"", 4, 2),
            Line("}", 2, 3),
            Line("}", 0, 4),
        ]);

        Assert.Equal("{\n  \"user\": {\n    \"name\": \"Ana\"\n  }\n}", text);
    }

    [Fact]
    public void A_larger_gap_between_lines_becomes_one_blank_line()
    {
        string text = TextLayout.Format([Line("first", 0, 0), Line("second", 0, 1), Line("third", 0, 3)]);

        Assert.Equal("first\nsecond\n\nthird", text);
    }

    [Fact]
    public void A_small_drift_is_not_an_indent()
    {
        var drifted = new OcrLineBox("slightly right", 20 + Char * 0.3, 10 + Pitch, "slightly right".Length * Char, 20);

        Assert.Equal("a line\nslightly right", TextLayout.Format([Line("a line", 0, 0), drifted]));
    }

    [Fact]
    public void No_lines_give_an_empty_text() => Assert.Equal("", TextLayout.Format([]));
}
