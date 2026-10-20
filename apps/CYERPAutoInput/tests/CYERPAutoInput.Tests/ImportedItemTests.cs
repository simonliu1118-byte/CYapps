using Xunit;

namespace CYERPAutoInput.Tests;

public class ImportedItemTests
{
    [Fact]
    public void Products_go_in_item_code_order_with_the_fee_lines_last()
    {
        List<ImportedItem> items =
        [
            new("X00329", "1", "120"), new("P902", "1", "-52"), new("BB00554", "1", "400"),
            new("X00110", "2", "120"), new("D901", "1", "-70"), new("E0118", "1", "140"), new("S100", "1", "70")
        ];
        var ordered = ImportedItem.InEntryOrder(items, ["D901", "S100", "P902", ""]);
        Assert.Equal(["BB00554", "E0118", "X00110", "X00329", "P902", "D901", "S100"], ordered.Select(i => i.ItemCode));
    }

    [Fact]
    public void A_bundle_stays_together_with_its_priced_line_first()
    {
        List<ImportedItem> items =
        [
            new("X00244", "2", "400") { SortKey = "X00244" }, new("X00011", "2", "0") { SortKey = "X00244" },
            new("X00100", "1", "50"), new("X00300", "1", "60")
        ];
        var ordered = ImportedItem.InEntryOrder(items, []);
        Assert.Equal(["X00100", "X00244", "X00011", "X00300"], ordered.Select(i => i.ItemCode));
    }

    [Theory]
    [InlineData("A00173(贈)", "A00173", true)]
    [InlineData("A00173（贈）", "A00173", true)]
    [InlineData("A00173 (贈品)", "A00173", true)]
    [InlineData("A00173", "A00173", false)]
    [InlineData("贈", "贈", false)]
    public void Reads_a_gift_mark(string part, string code, bool gift) =>
        Assert.Equal((code, gift), Bundles.SplitGift(part));

    [Fact]
    public void A_gift_part_leaves_the_other_part_sold_alone_at_the_bundle_price()
    {
        var problems = new List<string>();
        var items = Bundles.Expand([new("A00175+A00173(贈)", "2", "199")], "P900", problems);
        Assert.Empty(problems);
        Assert.Equal([new ImportedItem("A00175", "2", "199"), new ImportedItem("A00173", "0", "0") { GiftQuantity = "2" }], items);
    }

    [Fact]
    public void A_gift_beside_a_bundle_keeps_the_bundle_and_joins_a_sold_line_of_its_code()
    {
        var problems = new List<string>();
        var items = Bundles.Expand([new("X00244+X00011+X00100(贈)", "1", "400"), new("X00100", "3", "50")], "P900", problems);
        Assert.Empty(problems);
        Assert.Equal(
        [
            new ImportedItem("X00244", "1", "") { SortKey = "X00244", BundleTotal = "400" },
            new ImportedItem("X00011", "1", "") { SortKey = "X00244" },
            new ImportedItem("X00100", "3", "50") { GiftQuantity = "1" },
            new ImportedItem("P900", "1", "0") { TakesBundleGap = true }
        ], items);
    }

    [Fact]
    public void A_gift_alone_is_a_free_line()
    {
        var problems = new List<string>();
        var items = Bundles.Expand([new("A00173(贈)", "1", "0")], "P900", problems);
        Assert.Empty(problems);
        Assert.Equal([new ImportedItem("A00173", "0", "0") { GiftQuantity = "1" }], items);
    }
}
