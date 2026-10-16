using System.Drawing;
using Xunit;

namespace CYERPAutoQuery.Tests;

public class OcrWordFinderTests
{
    [Fact]
    public void FindsWordSplitIntoGlyphTokens()
    {
        OcrToken[] tokens = [new("售", new Rectangle(360, 6, 11, 12)), new("價", new Rectangle(372, 6, 11, 12))];
        Assert.Equal(new Rectangle(360, 6, 23, 12), OcrWordFinder.FindWord(tokens, "售價"));
    }

    [Fact]
    public void FindsWordInsideJoinedToken()
    {
        OcrToken[] tokens = [new("採購生管2售價", new Rectangle(270, 6, 140, 12))];
        var found = OcrWordFinder.FindWord(tokens, "售價");
        Assert.NotNull(found);
        Assert.Equal(370, found!.Value.Left);
    }

    [Fact]
    public void DistinguishesNumberedTabs()
    {
        OcrToken[] tokens = [new("基本資料1", new Rectangle(30, 6, 53, 12)), new("基本資料2", new Rectangle(109, 6, 53, 12))];
        var found = OcrWordFinder.FindWord(tokens, "基本資料2");
        Assert.Equal(109, found!.Value.Left);
    }

    [Fact]
    public void MissingWordIsNull() =>
        Assert.Null(OcrWordFinder.FindWord([new OcrToken("成本", new Rectangle(0, 0, 20, 12))], "售價"));

    [Fact]
    public void FindsEveryOccurrenceOnSeparateLines()
    {
        // 串查 lists 庫存狀況查詢作業 twice (user's screenshot 2026-10-16).
        var tokens = new[]
        {
            new OcrToken("庫存狀況查詢作業", new Rectangle(10, 60, 128, 16)),
            new OcrToken("庫存狀況查詢作業", new Rectangle(10, 20, 128, 16)),
            new OcrToken("批號管理", new Rectangle(10, 40, 64, 16)),
        };
        var found = OcrWordFinder.FindWords(tokens, "庫存狀況查詢作業");
        Assert.Equal(2, found.Count);
        Assert.Equal(20, found.Min(r => r.Top));
    }
}
