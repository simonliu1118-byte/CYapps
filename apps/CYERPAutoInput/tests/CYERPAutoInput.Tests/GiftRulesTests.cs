using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>設定 › 滿件贈送. Item codes are made up.</summary>
public class GiftRulesTests
{
    private static GiftRule R(string codes, int every, int gift, string giftItem, params string[] platforms) =>
        new() { ItemCodes = codes, Every = every, Gift = gift, GiftItem = giftItem, Platforms = [.. platforms] };

    [Fact]
    public void Every_full_set_of_one_item_gets_its_gift()
    {
        List<GiftRule> rules = [R("X00330", 10, 1, "", "蝦皮")];
        Assert.Empty(GiftRules.Gifts("蝦皮", [("X00330", "9")], rules));
        Assert.Equal(1, GiftRules.Gifts("蝦皮", [("X00330", "10")], rules)[0]);
        Assert.Equal(2, GiftRules.Gifts("蝦皮", [("X00001", "3"), ("X00330", "25")], rules)[1]);
        // The same code on two rows (two prices) is counted together, written on its first row.
        var split = GiftRules.Gifts("蝦皮", [("x00330", "6"), ("X00001", "1"), ("X00330", "4")], rules);
        Assert.Equal(1, Assert.Single(split).Value);
        Assert.Empty(GiftRules.Gifts("MO店+", [("X00330", "10")], rules));
        Assert.Equal(3, GiftRules.Gifts("酷澎", [("X00500", "6")], [R("X00500", 2, 1, "", "酷澎")])[0]);
    }

    [Fact]
    public void Several_items_count_together_and_the_gift_goes_where_set()
    {
        List<(string, string)> order = [("X00331", "3"), ("X00330", "2"), ("X00332", "5")];
        // To the one with the most (5 of X00332, the third row).
        var most = GiftRules.Gifts("蝦皮", order, [R("X00330, X00331, X00332", 10, 1, "", "蝦皮")]);
        Assert.Equal(1, most[2]);
        Assert.Single(most);
        // To the named one (X00330, the second row).
        Assert.Equal(1, GiftRules.Gifts("蝦皮", order, [R("X00330,X00331,X00332", 10, 1, "X00330", "蝦皮")])[1]);
        // Ties go to the card's order.
        Assert.Equal(1, GiftRules.Gifts("蝦皮", [("X00331", "5"), ("X00330", "5")], [R("X00330,X00331", 10, 1, "", "蝦皮")]).Keys.Single());
        // The named code is not in the order: the one with the most gets it, and it is said.
        var missing = new List<string>();
        var instead = GiftRules.Gifts("蝦皮", [("X00332", "4"), ("X00331", "6")], [R("X00330,X00331,X00332", 10, 1, "X00330", "蝦皮")], missing);
        Assert.Equal(1, instead[1]);
        Assert.Contains("X00331", Assert.Single(missing));
    }

    [Fact]
    public void Cards_are_checked_before_saving()
    {
        Assert.Empty(GiftRules.Problems([R("X00330,X00331", 10, 1, "X00330", "蝦皮"), R("X00340", 5, 1, "", "MO店+")]));
        // One code, one card, even for different platforms.
        Assert.Contains(GiftRules.Problems([R("X00330", 10, 1, "", "蝦皮"), R("X00330", 5, 1, "", "MO店+")]), p => p.Contains("只能有一個贈送規則"));
        Assert.Contains(GiftRules.Problems([R("X00330,X00331", 10, 1, "", "蝦皮"), R("x00331", 5, 1, "", "蝦皮", "酷澎")]), p => p.Contains("00331") && p.Contains("第 1 張和第 2 張"));
        Assert.Contains("第 1 張沒有品號。", GiftRules.Problems([R(" ", 10, 1, "", "蝦皮")]));
        Assert.Contains(GiftRules.Problems([R("X00330", 0, 1, "", "蝦皮")]), p => p.Contains("滿幾件"));
        Assert.Contains(GiftRules.Problems([R("X00330", 10, 0, "", "蝦皮")]), p => p.Contains("送幾件"));
        Assert.Contains(GiftRules.Problems([R("X00330", 10, 1, "")]), p => p.Contains("適用平台"));
        Assert.Contains(GiftRules.Problems([R("X00330,X00331", 10, 1, "X00999", "蝦皮")]), p => p.Contains("X00999"));
    }
}
