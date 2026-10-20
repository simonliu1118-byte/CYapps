using System.Drawing;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// INVQ01 rules and its 轉出到Excel workbook, rebuilt with fake values in the layout of the
/// user's samples (2026-10-16): one sheet 查詢資料, title in E1, titles in row 3 (several
/// padded with spaces, 庫存數量 twice), the item in A..X of row 4 only, the query's own
/// columns from Y on, one row each.
/// </summary>
public class Invq01Tests
{
    private static readonly string[] ItemTitles =
    [
        "品號", "品名", "規格", "會計", "POS介面", "商品", "生管", "主要廠商", "主要廠商名稱", "單位", "主要庫別", "主要庫別名稱",
        "生產線別", "生產線別名稱", "標準成本", "補貨政策", "固定前置天數", "變動前置天數", "品號屬性", "最近進價-本國幣別",
        "庫存數量", "庫存成本", "包裝單位", "庫存包裝數量"
    ];

    private static string Col(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    /// <summary>A 查詢資料 workbook: item part with <paramref name="code"/> on the first data row, then <paramref name="detail"/> rows.</summary>
    internal static byte[] FakeInvq01(string query, string code, string[] detailTitles, string[][] detail)
    {
        string Cell(int col, int row, string value) =>
            $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(value)}</t></is></c>";
        var rows = new StringBuilder();
        rows.Append($"<row r=\"1\">{Cell(4, 1, query)}</row>");
        rows.Append("<row r=\"3\">");
        var titles = ItemTitles.Concat(detailTitles).ToArray();
        for (var i = 0; i < titles.Length; i++) rows.Append(Cell(i, 3, titles[i]));
        rows.Append("</row>");
        var count = Math.Max(1, detail.Length);
        for (var r = 0; r < count; r++)
        {
            var rowNo = 4 + r;
            rows.Append($"<row r=\"{rowNo}\">");
            if (r == 0)
                for (var i = 0; i < ItemTitles.Length; i++)
                    rows.Append(Cell(i, rowNo, i == 0 ? code : i == 20 ? "999" : "x"));
            if (r < detail.Length)
                for (var i = 0; i < detail[r].Length; i++)
                    rows.Append(Cell(ItemTitles.Length + i, rowNo, detail[r][i]));
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
            Put("xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + rows + "</sheetData></worksheet>");
            Put("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"查詢資料\" sheetId=\"1\" r:id=\"rId3\" /></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
        }
        return ms.ToArray();
    }

    private static readonly string[] BatchTitles = ["批號     ", "庫存數量 ", "參考單號           ", "入庫日", "有效日期", "備註", "ME001"];
    private static readonly string[] HistoryTitles = ["日期 ", "單別-單號-序號               ", "庫別  ", "庫別名稱", "入出別   ", "異動數量 ", "單位成本 ", "異動金額 ", "備註"];

    private static IReadOnlyList<(string Name, List<string[]> Rows)> Read(byte[] bytes) => XlsxReader.ReadSheets(new MemoryStream(bytes));

    [Fact]
    public void ParsesBatchesFromTheirOwnColumns()
    {
        var bytes = FakeInvq01("查各批庫存量", "T0001", BatchTitles,
        [
            ["B001", "0", "X-1", "2025/06/10", "2028/03/18", "", "T0001"],
            ["B002", "42", "X-2", "2025/10/15", "2028/06/18", "", "T0001"],
        ]);
        var batches = Invq01Export.ParseBatches(Read(bytes), "T0001");
        Assert.Equal(2, batches.Count);
        Assert.Equal(["批號", "庫存數量", "入庫日", "有效日期"], batches[0].Select(f => f.Name));
        // 庫存數量 is the batch's (Z), not the item's (U = 999).
        Assert.Equal(["B002", "42", "2025/10/15", "2028/06/18"], batches[1].Select(f => f.Value));
    }

    [Fact]
    public void NoBatchesIsAnEmptyList()
    {
        var bytes = FakeInvq01("查各批庫存量", "T0001", BatchTitles, []);
        Assert.Empty(Invq01Export.ParseBatches(Read(bytes), "T0001"));
    }

    [Fact]
    public void ParsesHistoryWithoutCostsAndCompactsDocumentNo()
    {
        var bytes = FakeInvq01("查歷史異動", "T0001", HistoryTitles,
        [
            ["2021/06/22", "341A      -20210622001-0002", "W01", "測試倉", "入庫", "50", "255", "12750", "假備註"],
            ["2021/07/17", "231A      -20210717002-0003", "W01", "測試倉", "出庫", "1", "255", "255", ""],
        ]);
        var history = Invq01Export.ParseHistory(Read(bytes), "T0001");
        Assert.Equal(2, history.Count);
        Assert.Equal(ItemFieldCatalog.History.Select(c => c.Label), history[0].Select(f => f.Name));
        Assert.Equal("341A-20210622001-0002", history[0].Single(f => f.Name == "單別-單號-序號").Value);
        Assert.Equal("入庫", history[0].Single(f => f.Name == "入出別").Value);
        Assert.DoesNotContain(history[0], f => f.Name is "單位成本" or "異動金額");
    }

    [Fact]
    public void RejectsAnotherItemOrMissingColumns()
    {
        var other = FakeInvq01("查各批庫存量", "T0002", BatchTitles, [["B001", "1", "X", "2025/01/01", "2026/01/01", "", "T0002"]]);
        Assert.Throws<InvalidDataException>(() => Invq01Export.ParseBatches(Read(other), "T0001"));
        // Item codes are case-sensitive.
        var lower = FakeInvq01("查各批庫存量", "t0001", BatchTitles, []);
        Assert.Throws<InvalidDataException>(() => Invq01Export.ParseBatches(Read(lower), "T0001"));
        // A history export is not a batch export.
        var history = FakeInvq01("查歷史異動", "T0001", HistoryTitles, []);
        Assert.Throws<InvalidDataException>(() => Invq01Export.ParseBatches(Read(history), "T0001"));
    }

    [Theory]
    [InlineData("Y:需要", true)]
    [InlineData("T:需要且檢查", true)]
    [InlineData("N:不需要", false)]
    [InlineData(" N:不需要 ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BatchManagedUnlessN(string? value, bool expected) =>
        Assert.Equal(expected, Invq01Layout.IsBatchManaged(value));

    [Fact]
    public void TitleNamesTheQueryAfterStart()
    {
        Assert.True(Invq01Layout.IsInvq01Title("庫存狀況查詢作業(INVQ01)[假公司]"));
        Assert.False(Invq01Layout.TitleShowsQuery("庫存狀況查詢作業(INVQ01)[假公司]", Invq01Layout.BatchTab));
        Assert.True(Invq01Layout.TitleShowsQuery("庫存狀況查詢作業[假公司]-查各批庫存量", Invq01Layout.BatchTab));
        Assert.False(Invq01Layout.TitleShowsQuery("庫存狀況查詢作業[假公司]-查各批庫存量", Invq01Layout.HistoryTab));
        Assert.False(Invq01Layout.IsInvq01Title("品號資料建立作業(INVI02)[假公司]"));
    }

    [Fact]
    public void TabsUseMeasuredCenters()
    {
        var page = new Rectangle(100, 500, 1800, 300);
        Assert.Equal(new Point(503, 512), Invq01Layout.Tabs.PointFor(page, Invq01Layout.BatchTab));
        Assert.Equal(new Point(420, 512), Invq01Layout.Tabs.PointFor(page, Invq01Layout.HistoryTab));
        Assert.Equal(new Point(878, 512), Invq01Layout.Tabs.PointFor(page, Invq01Layout.ResultTab));
        Assert.Null(Invq01Layout.Tabs.PointFor(new Rectangle(0, 0, 300, 300), Invq01Layout.ResultTab));
        Assert.Equal(Invq01Layout.Tabs.Order.Length, Invq01Layout.Tabs.Centers!.Length);
    }

    [Fact]
    public void DatesUseEightPhysicalDigitsAndReadBackFromSegmentedMask()
    {
        var date = new DateOnly(2026, 1, 5);
        Assert.Equal("2026/01/05", Invq01Layout.ErpDate(date)); // UI text only
        Assert.Equal("20260105", Invq01Layout.ErpDateKeys(date)); // keyboard input
        Assert.True(Invq01Layout.ShowsDate("2026/01/05", date));
        Assert.True(Invq01Layout.ShowsDate("20260105", date));
        Assert.False(Invq01Layout.ShowsDate("2026/01/06", date));
        Assert.False(Invq01Layout.ShowsDate(null, date));
        // Regression: only digits are injected, not slash characters.
        Assert.Matches("^[0-9]{8}$", Invq01Layout.ErpDateKeys(date));
        Assert.Equal("20251001", Invq01Layout.ErpDateKeys(new DateOnly(2025, 10, 1)));
        Assert.Equal("20261001", Invq01Layout.ErpDateKeys(new DateOnly(2026, 10, 1)));
        Assert.Equal("20251016", Invq01Layout.ErpDateKeys(new DateOnly(2025, 10, 16)));
        Assert.Equal("20261016", Invq01Layout.ErpDateKeys(new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public void HistoryDateActionsFollowExactUserSpecifiedSequences()
    {
        var from = new DateOnly(2025, 10, 16);
        var to = new DateOnly(2026, 10, 16);

        // Only 起: click 起, type 20251016, no Enter.
        Assert.Equal(new Invq01Layout.HistoryDateAction(0, from, Click: true, EnterAfter: false),
            Assert.Single(Invq01Layout.DateActions(new HistoryRange(from, null))));

        // Only 迄: click 迄 directly, type 20261016 (user, 2026-10-16).
        Assert.Equal(new Invq01Layout.HistoryDateAction(1, to, Click: true, EnterAfter: false),
            Assert.Single(Invq01Layout.DateActions(new HistoryRange(null, to))));

        // Both: click 起, 20251016, Enter (jumps to 迄), 20261016 without clicking 迄.
        Assert.Equal(
            [new Invq01Layout.HistoryDateAction(0, from, Click: true, EnterAfter: true),
             new Invq01Layout.HistoryDateAction(1, to, Click: false, EnterAfter: false)],
            Invq01Layout.DateActions(new HistoryRange(from, to)));
        Assert.Equal("20251016", Invq01Layout.ErpDateKeys(from));

        // No dates: no mouse movement, no keys at all.
        Assert.Empty(Invq01Layout.DateActions(new HistoryRange(null, null)));
    }

    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void EmptyLocalHistoryDatesStayEmpty()
    {
        var empty = HistoryRange.Resolve(null, null, Today);
        Assert.Null(empty.From);
        Assert.Null(empty.To);

        var onlyTo = HistoryRange.Resolve(null, new DateOnly(2026, 3, 31), Today);
        Assert.Null(onlyTo.From);
        Assert.Equal(new DateOnly(2026, 3, 31), onlyTo.To);

        var onlyFrom = HistoryRange.Resolve(new DateOnly(2025, 10, 16), null, Today);
        Assert.Equal(new DateOnly(2025, 10, 16), onlyFrom.From);
        Assert.Null(onlyFrom.To);
    }

    [Fact]
    public void RangeHasNoMaximumSpan()
    {
        // The 3-year limit belongs to CY Web's module; the Agent offers the open query (user, 2026-10-16).
        Assert.Equal(new DateOnly(2015, 1, 1), HistoryRange.Resolve(new DateOnly(2015, 1, 1), null, Today).From);
        Assert.Throws<ArgumentException>(() => HistoryRange.Resolve(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1), Today));
        Assert.Throws<ArgumentException>(() => HistoryRange.Resolve(null, Today.AddDays(1), Today));
    }

    [Fact]
    public void ClaimCarriesHistoryOptions()
    {
        var job = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j1","itemNo":"T0001","expiresAt":"x","includeHistory":true,"historyFrom":"2026-01-01","historyTo":"2026-10-16"}},"requestId":"r"}""")!;
        Assert.True(job.IncludeHistory);
        var options = job.Options(Today);
        Assert.Equal(new HistoryRange(new DateOnly(2026, 1, 1), Today), options.History);
        // CY Web computes the dates. The Agent must not fill missing ones locally.
        var incomplete = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j5","itemNo":"T0001","includeHistory":true,"historyFrom":"2026-01-01","historyTo":null}}}""")!;
        Assert.Throws<ArgumentException>(() => incomplete.Options(Today));

        var plain = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j2","itemNo":"T0001","expiresAt":"x"}},"requestId":"r"}""")!;
        Assert.Null(plain.Options(Today).History);

        var bad = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j3","itemNo":"T0001","includeHistory":true,"historyFrom":"2020-01-01"}},"requestId":"r"}""")!;
        var ex = Assert.Throws<ArgumentException>(() => bad.Options(Today));
        Assert.Equal(ErpAgentErrorCode.INTERNAL, ErpAgentProtocol.Classify(ex));
        // A wrong type is ignored instead of losing the claimed job.
        var odd = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j4","itemNo":"T0001","includeHistory":"yes","historyFrom":5}},"requestId":"r"}""")!;
        Assert.False(odd.IncludeHistory);
        Assert.Null(odd.HistoryFrom);
    }

    private static ItemQueryResult Found(int historyRows, bool batchManaged = true) =>
        new ItemQueryResult("T0001", [new ErpField("品號", "T0001")], [], TimeSpan.FromSeconds(3))
        {
            BatchManaged = batchManaged,
            Batches = batchManaged ? [[new("批號", "B001"), new("庫存數量", "5"), new("入庫日", "2026/01/01"), new("有效日期", "2027/01/01")]] : [],
            HistoryRange = historyRows >= 0 ? new HistoryRange(new DateOnly(2026, 1, 1), Today) : null,
            History = Enumerable.Range(0, Math.Max(0, historyRows))
                .Select(i => (IReadOnlyList<ErpField>)ItemFieldCatalog.History.Select(c => new ErpField(c.Label, c.Label == "異動數量" ? i.ToString() : "v")).ToList())
                .ToList()
        };

    [Fact]
    public void FoundBodyCarriesBatchesAndHistoryTable()
    {
        var body = JsonNode.Parse(ErpAgentProtocol.FoundBody(Found(2), "v", includeCost: true))!;
        Assert.True(body["batchManaged"]!.GetValue<bool>());
        Assert.Equal("B001", body["batches"]![0]![0]!["value"]!.GetValue<string>());
        var history = body["history"]!;
        Assert.Equal("2026-01-01", history["from"]!.GetValue<string>());
        Assert.Equal("2026-10-16", history["to"]!.GetValue<string>());
        Assert.Equal(ItemFieldCatalog.History.Select(c => c.Label), history["columns"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(2, history["rows"]!.AsArray().Count);
        Assert.Equal("1", history["rows"]![1]![5]!.GetValue<string>());
        Assert.False(history["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void FoundBodyWithoutHistoryHasNull()
    {
        var body = JsonNode.Parse(ErpAgentProtocol.FoundBody(Found(-1, batchManaged: false), "v", includeCost: true))!;
        Assert.False(body["batchManaged"]!.GetValue<bool>());
        Assert.Empty(body["batches"]!.AsArray());
        Assert.Null(body["history"]);
    }

    [Fact]
    public void LongHistoryKeepsTheNewestRows()
    {
        var total = ErpAgentProtocol.MaxHistoryRows + 5;
        var history = JsonNode.Parse(ErpAgentProtocol.FoundBody(Found(total), "v", includeCost: true))!["history"]!;
        Assert.Equal(ErpAgentProtocol.MaxHistoryRows, history["rows"]!.AsArray().Count);
        Assert.Equal("5", history["rows"]![0]![5]!.GetValue<string>());
        Assert.Equal(total, history["total"]!.GetValue<int>());
        Assert.True(history["truncated"]!.GetValue<bool>());
    }
}
