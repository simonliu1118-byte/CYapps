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
}
