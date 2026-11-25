using System.Globalization;
using System.Windows;
using Xunit;
using Zarpazo.Editor;

namespace Zarpazo.Tests;

public class ShortcutTipConverterTests
{
    private static object? Part(string tip, string part) =>
        new ShortcutTipConverter().Convert(tip, typeof(string), part, CultureInfo.InvariantCulture);

    [Fact]
    public void Splits_name_key_and_detail()
    {
        string tip = "Texto (T). Enter confirma, Mayús+Enter salto de línea";
        Assert.Equal("Texto", Part(tip, "title"));
        Assert.Equal("T", Part(tip, "key"));
        Assert.Equal("Enter confirma, Mayús+Enter salto de línea", Part(tip, "detail"));
        Assert.Equal(Visibility.Visible, Part(tip, "keyShown"));
        Assert.Equal(Visibility.Visible, Part(tip, "detailShown"));
    }

    [Fact]
    public void A_key_alone_has_no_detail()
    {
        Assert.Equal("Deshacer", Part("Deshacer (Ctrl+Z)", "title"));
        Assert.Equal("Ctrl+Z", Part("Deshacer (Ctrl+Z)", "key"));
        Assert.Equal(Visibility.Collapsed, Part("Deshacer (Ctrl+Z)", "detailShown"));
    }

    [Fact]
    public void A_colon_also_starts_the_detail()
    {
        Assert.Equal("un clic por número", Part("Numeración de pasos (N): un clic por número", "detail"));
    }

    [Fact]
    public void A_long_parenthesis_is_not_a_key()
    {
        string tip = "Grabar esta zona como GIF o video (Ctrl+G empieza con lo último elegido)";
        Assert.Equal(tip, Part(tip, "title"));
        Assert.Equal(Visibility.Collapsed, Part(tip, "keyShown"));
    }

    [Fact]
    public void A_plain_tip_is_shown_whole()
    {
        Assert.Equal("Rojo", Part("Rojo", "title"));
        Assert.Equal(Visibility.Collapsed, Part("Rojo", "keyShown"));
    }
}
