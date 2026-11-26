using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>A bundle sold above its parts' ERP list prices is spread over the parts (user, 2026-11-26).</summary>
public class BundleSpreadTests
{
    [Fact]
    public void SpreadsInProportionAndAddsUpToWhatSold()
    {
        // List 393.5 (two parts), sold 500.
        var prices = BundlePrices.Spread([(250m, 1m), (143.5m, 1m)], 500m);
        Assert.Equal(318m, prices[0]);
        Assert.Equal(182m, prices[1]);
        Assert.Equal(500m, prices.Sum());
    }

    [Fact]
    public void QuantitiesAreCountedPerLine()
    {
        var parts = new List<(decimal List, decimal Quantity)> { (100m, 2m), (50m, 1m) };
        var prices = BundlePrices.Spread(parts, 300m);
        Assert.Equal(120m, prices[0]);
        Assert.Equal(60m, prices[1]);
        Assert.Equal(300m, prices[0] * 2 + prices[1]);
    }
}
