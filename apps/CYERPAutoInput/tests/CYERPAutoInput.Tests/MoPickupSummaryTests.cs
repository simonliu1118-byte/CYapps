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
    public void Prints_a_sheet_unless_all_of_it_was_printed()
    {
        var none = MoPickupSummary.Decide(["A", "B", "C"], []);
        Assert.True(none.Print);
        Assert.Empty(none.Void);
        var all = MoPickupSummary.Decide(["A", "B", "C"], ["A", "B", "C"]);
        Assert.False(all.Print);
        Assert.Empty(all.Void);
        // A printed before: B and C need the sheet; A's earlier sheet is to be voided.
        var (print, voided) = MoPickupSummary.Decide(["A", "B", "C"], ["A"]);
        Assert.True(print);
        Assert.Equal(["A"], voided);
    }

    [Fact]
    public void Counts_the_labels_of_a_label_pdf()
    {
        // Each label prints its barcode text twice (words of a real one-label PDF, 2026-10-14).
        Assert.Equal(1, MoPickupSummary.LabelCount(["D4969239660D", "D4969239660D", "寄件日/2026/10/14", "4969239660", "宅配單號"]));
        Assert.Equal(2, MoPickupSummary.LabelCount(["D4969239660D", "D4969239660D", "D4969239671D", "D4969239671D"]));
        Assert.Equal(0, MoPickupSummary.LabelCount(["宅配單號"]));
    }

    [Fact]
    public void Remembers_printed_sheets()
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
