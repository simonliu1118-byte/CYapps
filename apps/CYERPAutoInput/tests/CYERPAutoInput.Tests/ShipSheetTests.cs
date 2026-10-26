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
}
