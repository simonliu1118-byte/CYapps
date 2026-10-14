using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>設定 › 滿件贈送. Item codes are made up.</summary>
public class GiftRulesTests
{
    private static GiftRule R(string code, int every, int gift, params string[] platforms) =>
        new() { ItemCode = code, Every = every, Gift = gift, Platforms = [.. platforms] };

    [Fact]
    public void Every_full_set_of_the_item_alone_gets_its_gift()
    {
        List<GiftRule> rules = [R("X00330", 10, 1, "蝦皮")];
        Assert.Empty(GiftRules.Gifts("蝦皮", [("X00330", "9")], rules));
        Assert.Equal(1, GiftRules.Gifts("蝦皮", [("X00330", "10")], rules)[0]);
        Assert.Equal(2, GiftRules.Gifts("蝦皮", [("X00001", "3"), ("X00330", "25")], rules)[1]);
        // The same code on two rows (two prices) is counted together, written on its first row.
        var split = GiftRules.Gifts("蝦皮", [("x00330", "6"), ("X00001", "1"), ("X00330", "4")], rules);
        Assert.Equal(1, split[0]);
        Assert.Single(split);
        // Other platforms, or other items, get nothing.
        Assert.Empty(GiftRules.Gifts("MO店+", [("X00330", "10")], rules));
        Assert.Empty(GiftRules.Gifts("蝦皮", [("X00331", "10")], rules));
        Assert.Equal(3, GiftRules.Gifts("酷澎", [("X00500", "6")], [R("X00500", 2, 1, "酷澎")])[0]);
    }

    [Fact]
    public void Cards_are_checked_before_saving()
    {
        Assert.Empty(GiftRules.Problems([R("X00330", 10, 1, "蝦皮"), R("X00330", 5, 1, "MO店+")]));
        Assert.Contains(GiftRules.Problems([R("X00330", 10, 1, "蝦皮"), R("x00330", 5, 1, "蝦皮", "酷澎")]), p => p.Contains("第 1 張和第 2 張"));
        Assert.Contains("第 1 張沒有品號。", GiftRules.Problems([R(" ", 10, 1, "蝦皮")]));
        Assert.Contains(GiftRules.Problems([R("X00330", 0, 1, "蝦皮")]), p => p.Contains("滿幾件"));
        Assert.Contains(GiftRules.Problems([R("X00330", 10, 0, "蝦皮")]), p => p.Contains("送幾件"));
        Assert.Contains(GiftRules.Problems([R("X00330", 10, 1)]), p => p.Contains("適用平台"));
    }
}
