using Xunit;

namespace CYERPAutoQuery.Tests;

public class ItemCodeRulesTests
{
    [Theory]
    [InlineData("C00154", "C00154")]
    [InlineData("  C00154 ", "C00154")]
    [InlineData("abc123", "abc123")]
    public void AcceptsAndTrims(string input, string expected)
    {
        Assert.True(ItemCodeRules.TryNormalize(input, out var code, out _));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C00 154")]
    [InlineData("C00\t154")]
    [InlineData("C0015\n4")]
    public void RejectsEmptyOrSpaces(string? input) =>
        Assert.False(ItemCodeRules.TryNormalize(input, out _, out _));

    [Theory]
    [InlineData("A-01")]
    [InlineData("A01/2")]
    [InlineData("C00154.")]
    [InlineData("Ｃ00154")]
    [InlineData("穴位貼")]
    [InlineData("C00154'")]
    public void RejectsAnythingButAsciiLettersAndDigits(string input)
    {
        Assert.False(ItemCodeRules.TryNormalize(input, out _, out var error));
        Assert.Equal("品號只能輸入英文字母與數字。", error);
    }

    [Fact]
    public void RejectsTooLong() =>
        Assert.False(ItemCodeRules.TryNormalize(new string('A', ItemCodeRules.MaxLength + 1), out _, out _));

    [Theory]
    [InlineData("C00154", "C00154", true)]
    [InlineData("C00154 ", "C00154", true)]
    [InlineData("c00154", "C00154", false)]
    [InlineData("C00155", "C00154", false)]
    [InlineData("", "C00154", false)]
    [InlineData(null, "C00154", false)]
    public void SameCode(string? shown, string requested, bool expected) =>
        Assert.Equal(expected, ItemCodeRules.SameCode(shown, requested));
}
