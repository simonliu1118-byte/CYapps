using System.Drawing;
using Xunit;

namespace CYERPAutoInput.Tests;

public sealed class RibbonPrintLocatorTests
{
    [Fact]
    public void Joins_one_glyph_tokens_on_a_line()
    {
        // As the PC returns the Ribbon: one token per glyph.
        List<(string, Rectangle)> tokens =
        [
            ("新", new Rectangle(8, 110, 12, 16)), ("增", new Rectangle(21, 110, 12, 16)),
            ("列", new Rectangle(157, 110, 12, 16)), ("印", new Rectangle(170, 110, 12, 16)),
            ("上", new Rectangle(224, 81, 12, 16)), ("筆", new Rectangle(237, 81, 12, 16))
        ];
        Assert.Equal(new Rectangle(157, 110, 25, 16), RibbonPrintLocator.FindWord(tokens, "列印"));
    }

    [Fact]
    public void Finds_the_word_inside_a_joined_token()
    {
        Assert.Equal(new Rectangle(155, 110, 30, 16), RibbonPrintLocator.FindWord([("列印", new Rectangle(155, 110, 30, 16))], "列印"));
        Assert.Equal(new Rectangle(140, 110, 40, 16), RibbonPrintLocator.FindWord([("修改列印", new Rectangle(100, 110, 80, 16))], "列印"));
    }

    [Fact]
    public void Does_not_join_glyphs_on_other_lines_or_far_apart()
    {
        Assert.Null(RibbonPrintLocator.FindWord([("列", new Rectangle(157, 110, 12, 16)), ("印", new Rectangle(170, 140, 12, 16))], "列印"));
        Assert.Null(RibbonPrintLocator.FindWord([("列", new Rectangle(157, 110, 12, 16)), ("印", new Rectangle(260, 110, 12, 16))], "列印"));
    }

    [Fact]
    public void Finds_the_direct_print_item()
    {
        List<(string, Rectangle)> menu =
        [
            ("直", new Rectangle(40, 10, 12, 16)), ("接", new Rectangle(53, 10, 12, 16)), ("列", new Rectangle(66, 10, 12, 16)), ("印", new Rectangle(79, 10, 12, 16)),
            ("預", new Rectangle(40, 40, 12, 16)), ("覽", new Rectangle(53, 40, 12, 16)), ("列", new Rectangle(66, 40, 12, 16)), ("印", new Rectangle(79, 40, 12, 16))
        ];
        Assert.Equal(new Rectangle(40, 10, 25, 16), RibbonPrintLocator.FindWord(menu, "直接"));
    }
}
