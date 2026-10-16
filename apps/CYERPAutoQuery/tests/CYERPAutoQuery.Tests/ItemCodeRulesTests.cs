using Xunit;

namespace CYERPAutoQuery.Tests;

public class ItemCodeRulesTests
{
    [Theory]
    [InlineData("C00154", "C00154")]
    [InlineData("  C00154 ", "C00154")]
    [InlineData("A-01/2", "A-01/2")]
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

    [Fact]
    public void RejectsTooLong() =>
        Assert.False(ItemCodeRules.TryNormalize(new string('A', ItemCodeRules.MaxLength + 1), out _, out _));

    [Theory]
    [InlineData("C00154", "C00154", true)]
    [InlineData("c00154 ", "C00154", true)]
    [InlineData("C00155", "C00154", false)]
    [InlineData("", "C00154", false)]
    [InlineData(null, "C00154", false)]
    public void SameCode(string? shown, string requested, bool expected) =>
        Assert.Equal(expected, ItemCodeRules.SameCode(shown, requested));
}
