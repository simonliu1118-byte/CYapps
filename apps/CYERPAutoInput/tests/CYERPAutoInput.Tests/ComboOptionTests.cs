using Xunit;
using static CYERPAutoInput.InputRules;

namespace CYERPAutoInput.Tests;

public class ComboOptionTests
{
    [Theory]
    [InlineData("1:應稅內含", "1")]
    [InlineData("7：電子發票", "7")]
    [InlineData("免稅", "免稅")]
    public void Code_is_the_part_before_the_colon(string option, string code) =>
        Assert.Equal(code, ComboOptionCode(option));

    [Theory]
    [InlineData("7:電子發票", "7:電子發票")]
    [InlineData("7：電子發票", "7:電子發票")]
    [InlineData("7:電子發黑", "7:電子發票")] // OCR misread of the label, same code
    [InlineData("7電子發票", "7:電子發票")]  // OCR dropped the colon
    [InlineData(" 1:應稅內含 ", "1:應稅內含")]
    public void Shown_text_with_the_same_code_matches(string shown, string option) =>
        Assert.True(ComboShowsOption(shown, option));

    [Theory]
    [InlineData("1:二聯式", "7:電子發票")]
    [InlineData("", "7:電子發票")]
    [InlineData(null, "7:電子發票")]
    [InlineData("71:其他", "7:電子發票")]
    [InlineData("免稅", "應稅")]
    public void Different_or_missing_text_does_not_match(string? shown, string option) =>
        Assert.False(ComboShowsOption(shown, option));
}
