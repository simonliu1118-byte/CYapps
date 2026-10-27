using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>品名 search rules, the 資料瀏覽 export (fake values in the layout of the user's sample) and the Web shapes.</summary>
public class ItemNameSearchTests
{
    [Theory]
    [InlineData("穴位貼", "穴位貼")]
    [InlineData("  磁力 穴位貼 ", "磁力 穴位貼")]
    [InlineData("<停產>", "<停產>")]
    public void AcceptsPlainKeywords(string input, string expected)
    {
        Assert.True(ItemNameRules.TryNormalize(input, out var keyword, out _));
        Assert.Equal(expected, keyword);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a'b")]
    [InlineData("50%")]
    [InlineData("a_b")]
    [InlineData("[a]")]
    [InlineData("a\tb")]
    public void RejectsQuotesWildcardsAndControls(string input) =>
        Assert.False(ItemNameRules.TryNormalize(input, out _, out _));

    [Fact]
    public void RejectsTooLong() =>
        Assert.False(ItemNameRules.TryNormalize(new string('穴', ItemNameRules.MaxLength + 1), out _, out _));

    [Fact]
    public void ConditionIsErpsOwnForm()
    {
        // As ERP writes it in 進階設定 (user's screenshot 2026-10-16).
        Assert.Equal("(INVMB.MB002 like N'%穴位貼%')", ItemNameRules.Condition("穴位貼"));
        Assert.True(ItemNameRules.SameCondition("(INVMB.MB002 like N'%穴位貼%')\r\n", ItemNameRules.Condition("穴位貼")));
        Assert.False(ItemNameRules.SameCondition("", ItemNameRules.Condition("穴位貼")));
    }

    [Theory]
    [InlineData("MAGRAIN日本阪村穴位貼(300入)", "穴位貼", true)]
    [InlineData("magrain貼布", "MAGRAIN", true)]
    [InlineData("ＭＡＧＲＡＩＮ貼布", "magrain", true)]
    [InlineData("磁力貼", "穴位貼", false)]
    public void NameMatchFollowsSqlLike(string name, string keyword, bool expected) =>
        Assert.Equal(expected, ItemNameRules.NameContains(name, keyword));

    internal static byte[] FakeList(params (string Code, string Name)[] items)
    {
        string Cell(string col, int row, string value) =>
            $"<c r=\"{col}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(value)}</t></is></c>";
        string[] titles = ["品號", "品名", "規格", "版次", "單位", "庫存數量", "庫存金額", "單位成本", "標準售價"];
        var cols = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I" };
        var rows = new StringBuilder($"<row r=\"1\">{Cell("E", 1, "品號資料建立作業")}</row><row r=\"3\">");
        for (var i = 0; i < titles.Length; i++) rows.Append(Cell(cols[i], 3, titles[i]));
        rows.Append("</row>");
        for (var r = 0; r < items.Length; r++)
        {
            var n = 4 + r;
            string[] values = [items[r].Code, items[r].Name, "假規格", "0000", "包", "12", "999", "9.9", "100"];
            rows.Append($"<row r=\"{n}\">");
            for (var i = 0; i < values.Length; i++) rows.Append(Cell(cols[i], n, values[i]));
            rows.Append("</row>");
        }
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Put("xl/worksheets/sheet1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + rows + "</sheetData></worksheet>");
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"單頭資料\" sheetId=\"1\" r:id=\"rId3\" /></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
        }
        return ms.ToArray();
    }

    internal static IReadOnlyList<(string Name, List<string[]> Rows)> Read(byte[] bytes) => XlsxReader.ReadSheets(new MemoryStream(bytes));

    [Fact]
    public void ListsEveryItemWithSearchColumnsOnly()
    {
        var items = ItemSearchExport.Parse(Read(FakeList(("T0001", "假穴位貼A"), ("T0002", "穴位貼B"))), "穴位貼");
        Assert.Equal(2, items.Count);
        Assert.Equal(ItemFieldCatalog.SearchList.Select(c => c.Label), items[0].Select(f => f.Name));
        Assert.Equal("T0002", ItemSearchResult.Value(items[1], "品號"));
        Assert.DoesNotContain(items[0], f => f.Name is "庫存金額" or "單位成本");
    }

    [Fact]
    public void RefusesAListThatIgnoredTheCondition()
    {
        // A row whose 品名 lacks the keyword means ERP did not apply the condition.
        Assert.Throws<InvalidDataException>(() =>
            ItemSearchExport.Parse(Read(FakeList(("T0001", "假穴位貼A"), ("T0003", "別的商品"))), "穴位貼"));
    }

    [Fact]
    public void TruncatedWhenErpLimitReached()
    {
        IReadOnlyList<ErpField> Row(int i) => [new("品號", $"T{i:0000}"), new("品名", "穴位貼")];
        Assert.True(new ItemSearchResult("穴位貼", Enumerable.Range(0, 500).Select(Row).ToList(), 500, TimeSpan.Zero).Truncated);
        Assert.False(new ItemSearchResult("穴位貼", [Row(1)], 500, TimeSpan.Zero).Truncated);
        Assert.False(new ItemSearchResult("穴位貼", [Row(1)], null, TimeSpan.Zero).Truncated);
    }

    [Fact]
    public void ClaimCarriesNameSearch()
    {
        var job = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j1","queryType":"ITEM_NAME","keyword":"穴位貼","expiresAt":"x"}},"requestId":"r"}""")!;
        Assert.True(job.IsNameSearch);
        Assert.Equal("穴位貼", job.Keyword);
        var plain = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j2","itemNo":"T0001"}},"requestId":"r"}""")!;
        Assert.False(plain.IsNameSearch);
        Assert.Throws<InvalidDataException>(() => ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j3","queryType":"ITEM_NAME"}},"requestId":"r"}"""));
    }

    [Fact]
    public void SearchBodyListsItems()
    {
        IReadOnlyList<ErpField> row = [new("品號", "T0001"), new("品名", "假穴位貼")];
        var found = JsonNode.Parse(ErpAgentProtocol.SearchBody(new ItemSearchResult("穴位貼", [row], 500, TimeSpan.FromSeconds(2)), "v"))!;
        Assert.Equal("FOUND", found["outcome"]!.GetValue<string>());
        Assert.Equal("T0001", found["items"]![0]![0]!["value"]!.GetValue<string>());
        Assert.Empty(found["fields"]!.AsArray());
        Assert.False(found["truncated"]!.GetValue<bool>());
        var none = JsonNode.Parse(ErpAgentProtocol.SearchBody(new ItemSearchResult("穴位貼", [], 500, TimeSpan.Zero), "v"))!;
        Assert.Equal("NOT_FOUND", none["outcome"]!.GetValue<string>());
        Assert.Empty(none["items"]!.AsArray());
    }

    [Fact]
    public void ItemCodesAreOrEdTheWayErpWritesThem()
    {
        Assert.Equal("(INVMB.MB001 = N'X00001')\r\n  OR (INVMB.MB001 = N'X00315-1')", ItemCodeConditions.Condition(["X00001", "X00315-1"]));
        Assert.True(ItemNameRules.SameCondition("(INVMB.MB001 = N'X00001')\n OR (INVMB.MB001 = N'X00315-1')", ItemCodeConditions.Condition(["X00001", "X00315-1"])));
        Assert.False(ItemCodeConditions.IsUsable("X'1"));
        Assert.False(ItemCodeConditions.IsUsable("X 1"));
        Assert.False(ItemCodeConditions.IsUsable("貼布"));
        Assert.Throws<ArgumentException>(() => ItemCodeConditions.Condition(["X0001", "X'1"]));
    }

    [Fact]
    public void ABatchFitsTheBoxAndTheCount()
    {
        var codes = Enumerable.Range(1, 250).Select(i => $"X{i:00000}").ToList();
        Assert.Equal(ItemCodeConditions.MaxBatch, ItemCodeConditions.Batch(codes, null, ItemCodeConditions.MaxBatch));
        var one = ItemCodeConditions.Condition(codes.Take(1)).Length;
        var fits = ItemCodeConditions.Batch(codes, 300, ItemCodeConditions.MaxBatch);
        Assert.True(ItemCodeConditions.Condition(codes.Take(fits)).Length <= 300);
        Assert.True(ItemCodeConditions.Condition(codes.Take(fits + 1)).Length > 300);
        Assert.Equal(1, ItemCodeConditions.Batch(codes, one - 1, 50));
        Assert.Equal(3, ItemCodeConditions.Batch(codes.Take(3).ToList(), null, 50));
    }

    [Fact]
    public void AnItemCodeListKeepsOnlyTheAskedCodes()
    {
        var items = ItemCodeConditions.Parse(Read(FakeList(("T0001", "假品A"), ("T0002", "假品B"))), ["T0001", "T0002", "T0009"]);
        Assert.Equal(["T0001", "T0002"], items.Select(i => ItemSearchResult.Value(i, "品號")));
        Assert.Equal("12", ItemSearchResult.Value(items[0], "庫存數量"));
        Assert.Throws<InvalidDataException>(() => ItemCodeConditions.Parse(Read(FakeList(("T0001", "假品A"), ("T0003", "假品C"))), ["T0001", "T0002"]));
    }
}
