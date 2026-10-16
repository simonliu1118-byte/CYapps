using Xunit;

namespace CYERPAutoQuery.Tests;

public class ItemQueryResultTests
{
    [Fact]
    public void PositionIsOneBased() =>
        Assert.Equal("第 2 列第 3 欄", new ErpFieldValue("售價", 1, 2, "TDBEdit", "450").Position);

    [Fact]
    public void TsvFlattensTabsAndNewLines()
    {
        var result = new ItemQueryResult("X1", "名", "規", [
            new ErpFieldValue("表頭", 0, 0, "TDBEdit", "X1"),
            new ErpFieldValue("基本資料1", 0, 1, "TDBMemo", "a\tb\r\nc")
        ], TimeSpan.FromSeconds(1));
        var lines = result.ToTsv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("頁籤\t位置\t類型\t內容", lines[0]);
        Assert.Equal("基本資料1\t第 1 列第 2 欄\tTDBMemo\ta b / c", lines[2]);
    }
}
