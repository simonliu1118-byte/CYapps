using Xunit;

namespace CYERPAutoInput.Tests;

public class MoPickupSummaryTests
{
    [Fact]
    public void Reads_the_sheet_rows()
    {
        // Words of a real 到廠取貨總表 (user, 2026-10-14), one order; a second row added.
        string[] words =
        [
            "momo第三方物流到廠取貨總表", "供應商名稱", "某某行", "列印日期", "2026/10/14", "時間", "16:42:40", "NO", "宅配單號", "出貨單號", "收件人", "件數", "代收款",
            "1", "4969239660", "10000013260614", "吳*珊", "1", "0",
            "2", "4969239671", "10000013260615", "王*明", "2", "0",
            "合計件數", "合計代收款", "3", "0"
        ];
        Assert.Equal(["4969239660", "4969239671"], MoPickupSummary.Numbers(words));
        Assert.Empty(MoPickupSummary.Numbers(["0971191888", "客戶代碼2736592566"]));
    }

    [Fact]
    public void A_sheet_with_an_order_printed_before_is_not_printed_again()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pickup-{Guid.NewGuid():N}.json");
        try
        {
            var ledger = new PickupSummaryLedger(path);
            Assert.Empty(ledger.AlreadyPrinted(["A"]));
            ledger.Record(["A"]);
            var again = new PickupSummaryLedger(path);
            Assert.Equal(["A"], again.AlreadyPrinted(["A", "B", "C"]));
            Assert.Empty(again.AlreadyPrinted(["B", "C"]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
