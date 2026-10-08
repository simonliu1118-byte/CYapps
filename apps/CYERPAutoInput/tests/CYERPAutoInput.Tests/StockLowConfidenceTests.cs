using Xunit;
using static CYERPAutoInput.InputRules;

namespace CYERPAutoInput.Tests;

public class StockLowConfidenceTests
{
    private static RecognizedChar C(string text, float p, params (string, float)[] alternatives) => new(text, p, alternatives);

    [Fact]
    public void Slashed_zero_from_the_real_erp_cell_is_accepted()
    {
        // V0.2.0 Build 3 real F2 first row: 0 (0.49), blank (0.18), O (0.04).
        Assert.True(TryAcceptLowConfidenceStock([C("0", 0.49f, ("", 0.18f), ("O", 0.04f))], out var text));
        Assert.Equal("0", text);
    }

    [Fact]
    public void Zero_shaped_letter_maps_to_zero()
    {
        Assert.True(TryAcceptLowConfidenceStock([C("O", 0.48f, ("0", 0.30f))], out var text));
        Assert.Equal("0", text);
    }

    [Fact]
    public void A_rival_digit_rejects_the_read()
    {
        Assert.False(TryAcceptLowConfidenceStock([C("0", 0.40f, ("8", 0.20f))], out _));
        Assert.False(TryAcceptLowConfidenceStock([C("5", 0.90f), C("O", 0.35f, ("6", 0.10f))], out _));
    }

    [Fact]
    public void Very_low_or_non_numeric_characters_are_rejected()
    {
        Assert.False(TryAcceptLowConfidenceStock([C("0", 0.15f)], out _));
        Assert.False(TryAcceptLowConfidenceStock([C("=", 0.90f)], out _));
        Assert.False(TryAcceptLowConfidenceStock([C("A", 0.60f)], out _));
        Assert.False(TryAcceptLowConfidenceStock([], out _));
    }

    [Fact]
    public void Multi_digit_low_confidence_read_without_rivals_is_accepted()
    {
        Assert.True(TryAcceptLowConfidenceStock([C("1", 0.55f), C("0", 0.45f, ("O", 0.2f)), C("Q", 0.3f, ("0", 0.25f))], out var text));
        Assert.Equal("100", text);
    }
}
