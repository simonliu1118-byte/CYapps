using CYERPAutoInput;
using Xunit;

namespace CYERPAutoInput.Tests;

public sealed class ShipSheetTests
{
    [Fact]
    public void Lines_are_read_by_their_column_titles()
    {
        List<string[]> header = [["銷貨單建立作業"], [], ["單別", "單號", "客戶代號"], ["234", "20261026038", "C001"]];
        List<string[]> detail =
        [
            ["銷貨單建立作業"], [],
            ["單別", "單號", "序號", "品號", "品名", "規格", "庫別", "數量", "單位", "單價", "金額"],
            ["234", "20261026038", "0001", "A00014", "弧形拔罐杯 6號", "", "KH01", "5", "個", "40", "200"],
            ["234", "20261026038", "0002", "Z902", "促銷組合", "", "", "1", "", "-6", "-6"],
            []
        ];
        var lines = ShipSheetExport.Lines([("單頭資料", header), ("單身資料", detail)]);
        Assert.Equal(2, lines.Count);
        Assert.Equal(new ShipSheetLine("A00014", "弧形拔罐杯 6號", "5", "個", "40", "200"), lines[0]);
        var sheet = new ShipSheet("234-20261026038", "蝦皮", "261023DBHG8DC6", "TW262783558974J", "蝦皮店到店", lines, "", DateTime.Now);
        Assert.Equal(194m, sheet.Total);
        Assert.Throws<InvalidDataException>(() => ShipSheetExport.Lines([("單頭資料", header)]));
    }

    [Fact]
    public void Numbers_split_and_show()
    {
        Assert.Equal(("234-20261026", "038"), ShipSheet.SplitTail("234-20261026038"));
        Assert.Equal((string.Empty, "12"), ShipSheet.SplitTail("12"));
        Assert.Equal("1,234", ShipSheet.Show("1234.000"));
        Assert.Equal("12.5", ShipSheet.Show("12.50"));
        Assert.Equal("-6", ShipSheet.Show("-6"));
    }

    [Fact]
    public void A_document_opened_by_hand_is_read_from_its_export()
    {
        List<string[]> header = [["銷貨單建立作業"], [], ["單別", "單號", "備註", "送貨地址(一)", "貨運別"], ["234", "20261026101", "蝦皮訂單261026M2KX1EQD", "TW269346516980B", "07"]];
        List<string[]> detail = [["銷貨單建立作業"], [], ["品號", "品名", "數量", "單位", "單價", "金額"], ["A00023", "志遠拔罐器組(紅盒)", "1", "組", "649", "649"]];
        var freight = new Dictionary<string, string> { ["蝦皮店到店"] = "07", ["新竹物流"] = "03" };
        var sheet = ShipSheetExport.FromDocument([("單頭資料", header), ("單身資料", detail)],
            [("蝦皮", "蝦皮訂單"), ("MO店+", "MO店+訂單")], freight, p => p == "蝦皮" ? "註記" : "", DateTime.Now);
        Assert.Equal("234-20261026101", sheet.Document);
        Assert.Equal("蝦皮", sheet.Platform);
        Assert.Equal("261026M2KX1EQD", sheet.OrderSn);
        Assert.Equal("TW269346516980B", sheet.Tracking);
        Assert.Equal("蝦皮店到店", sheet.Carrier);
        Assert.Equal("註記", sheet.Note);
        Assert.Single(sheet.Lines);

        // Entered by hand, with no platform 備註: only the document.
        header[3] = ["234", "20261026102", "林先生自取", "", "99"];
        var plain = ShipSheetExport.FromDocument([("單頭資料", header), ("單身資料", detail)], [("蝦皮", "蝦皮訂單")], freight, _ => "", DateTime.Now);
        Assert.Equal((string.Empty, string.Empty, "99"), (plain.Platform, plain.OrderSn, plain.Carrier));
    }
}
