using Xunit;

namespace CYERPAutoQuery.Tests;

public class ItemQueryResultTests
{
    private static ItemQueryResult Sample() => new("X1",
        [new ErpField("品號", "X1"), new ErpField("品名", "假品名"), new ErpField("規格", "規\t格"), new ErpField("商品描述", "a\nb")],
        [[new ErpField("庫別", "W1"), new ErpField("庫存數量", "5")]],
        TimeSpan.FromSeconds(1));

    [Fact]
    public void NamedValues()
    {
        var r = Sample();
        Assert.Equal("假品名", r.ItemName);
        Assert.Equal("規\t格", r.Specification);
        Assert.Equal(string.Empty, r.Value("不存在"));
        Assert.True(r.Found);
    }

    [Fact]
    public void TsvListsFieldsThenWarehouses()
    {
        var lines = Sample().ToTsv().Split("\r\n");
        Assert.Equal("欄位\t內容", lines[0]);
        Assert.Equal("規格\t規 格", lines[3]);
        Assert.Equal("商品描述\ta b", lines[4]);
        Assert.Equal("", lines[5]);
        Assert.Equal("庫別\t庫存數量", lines[6]);
        Assert.Equal("W1\t5", lines[7]);
    }

    [Fact]
    public void NotFoundHasNoFields()
    {
        var result = ItemQueryResult.NotFound("X9", TimeSpan.FromSeconds(2));
        Assert.False(result.Found);
        Assert.Empty(result.Fields);
        Assert.Empty(result.Warehouses);
        Assert.Equal("X9", result.ItemCode);
    }
}
