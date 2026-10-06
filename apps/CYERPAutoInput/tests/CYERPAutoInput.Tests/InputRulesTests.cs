using Xunit;

namespace CYERPAutoInput.Tests;

public class InputRulesTests
{
    [Theory]
    [InlineData("2026/10/06", "20261006")]
    [InlineData("20261006", "20261006")]
    [InlineData("2026-10-06 extra 99", "20261006")]
    public void NormalizeDateDigits_keeps_first_eight_digits(string input, string expected) =>
        Assert.Equal(expected, InputRules.NormalizeDateDigits(input));

    [Theory]
    [InlineData("2026", "2026")]
    [InlineData("202610", "2026/10")]
    [InlineData("20261006", "2026/10/06")]
    public void FormatDateForDisplay_inserts_slashes_progressively(string digits, string expected) =>
        Assert.Equal(expected, InputRules.FormatDateForDisplay(digits));

    [Theory]
    [InlineData("2026/10/06", true, "20261006")]
    [InlineData("2024/02/29", true, "20240229")]
    [InlineData("2026/02/29", false, "20260229")]
    [InlineData("2026/13/01", false, "20261301")]
    [InlineData("2026/10", false, "202610")]
    public void TryNormalizeValidDate_requires_a_real_calendar_date(string input, bool valid, string digits)
    {
        Assert.Equal(valid, InputRules.TryNormalizeValidDate(input, out var normalized));
        Assert.Equal(digits, normalized);
    }

    [Theory]
    [InlineData("20261006001", true)]
    [InlineData("20261006999", true)]
    [InlineData("20261006000", false)]
    [InlineData("20261005001", false)]
    [InlineData("2026100601", false)]
    [InlineData("2026100600A", false)]
    [InlineData("", false)]
    public void IsExpectedSalesOrderNumber_matches_date_and_sequence(string value, bool expected) =>
        Assert.Equal(expected, InputRules.IsExpectedSalesOrderNumber(value, "20261006"));

    [Theory]
    [InlineData("67", 67)]
    [InlineData("100.0000", 100)]
    [InlineData("1,234", 1234)]
    [InlineData(" 1 234 ", 1234)]
    [InlineData("-5", -5)]
    public void TryParseStockText_parses_ocr_stock_numbers(string raw, decimal expected)
    {
        Assert.True(InputRules.TryParseStockText(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("批號")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseStockText_rejects_non_numbers(string raw) =>
        Assert.False(InputRules.TryParseStockText(raw, out _));
}
