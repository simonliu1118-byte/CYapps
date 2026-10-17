using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>Automatic scan of the Shopee order page; all page content here is made up.</summary>
public sealed class ShopeePageScanTests
{
    // A page of distinct rows, seen through a 300-row window with a 40-row fixed bar on top.
    private static readonly long[] PageRows = Enumerable.Range(0, 2000).Select(i => (long)(i * 7919 % 1000 + (i / 50) * 13)).ToArray();

    private static List<long> View(int scroll) =>
        Enumerable.Range(0, 40).Select(i => 5_000_000L + i).Concat(Enumerable.Range(0, 260).Select(i => PageRows[scroll + 40 + i])).ToList();

    [Fact]
    public void Measures_the_scroll_below_the_fixed_bar()
    {
        Assert.Equal(40, ShopeePageScan.StaticTopRows(View(0), View(120)));
        Assert.Equal(120, ShopeePageScan.FindScrollOffset(View(0), View(120), 40));
        Assert.Equal(0, ShopeePageScan.FindScrollOffset(View(500), View(500), 40));
    }

    [Fact]
    public void Never_takes_an_unmoved_screen_for_a_fixed_band()
    {
        // At the bottom of the page two captures are identical (Build 6 on the user's PC).
        Assert.True(ShopeePageScan.SameScreen(View(500), View(500)));
        Assert.True(ShopeePageScan.StaticTopRows(View(500), View(500)) <= 300 * 2 / 5);
    }

    [Fact]
    public void Tolerates_a_header_shadow_and_hover_rows()
    {
        // After scrolling, a few rows under the fixed bar and around the cursor look different.
        var next = View(120).Select((v, i) => i is >= 40 and < 46 || i is >= 200 and < 210 ? v + 50_000 + i : v).ToList();
        Assert.Equal(120, ShopeePageScan.FindScrollOffset(View(0), next, 40));
    }

    [Fact]
    public void Measures_the_scroll_from_the_text_positions()
    {
        List<OcrBox> before = [new("瀏覽器", 0, 5, 80, 20), new("訂單編號", 338, 650, 96, 21), new("26100000TEST", 367, 684, 121, 17), new("運送資訊", 337, 724, 94, 21), new("共3個商品", 370, 806, 90, 20)];
        List<OcrBox> after = [new("瀏覽器", 0, 5, 80, 20), new("訂單編號", 338, 250, 96, 21), new("26100000TEST", 367, 285, 121, 17), new("運送資訊", 337, 324, 94, 21), new("共3個商品", 370, 406, 90, 20)];
        Assert.Equal(400, ShopeePageScan.FindScrollOffsetByText(before, after, 177));
        Assert.Equal(-1, ShopeePageScan.FindScrollOffsetByText(before, [new("其他", 10, 300, 40, 20)], 177));
    }

    [Fact]
    public void Reports_no_fit_when_the_page_changed()
    {
        var other = View(0).Select((v, i) => i < 40 ? v : v + 97 * i).ToList();
        Assert.Equal(-1, ShopeePageScan.FindScrollOffset(View(0), other, 40));
    }

    [Fact]
    public void Places_captures_on_the_page_without_duplicating_the_overlap()
    {
        var page = new List<OcrBox>();
        ShopeePageScan.AddCapture(page, [new OcrBox("瀏覽器", 0, 5, 80, 20), new OcrBox("商品選項貨號：X00001", 100, 250, 130, 16)], 0, 40);
        // The next capture, scrolled 200: the same line now at 50, read slightly differently.
        ShopeePageScan.AddCapture(page, [new OcrBox("商品項貨號：X00001", 100, 51, 128, 16), new OcrBox("商品選項貨號：X00002", 100, 180, 130, 16)], 200, 40);
        Assert.Equal(["商品選項貨號：X00001", "商品選項貨號：X00002"], page.Select(b => b.Text));
        Assert.Equal(380, page[1].Top);
    }

    [Fact]
    public void Cuts_the_three_parts_out_of_the_page()
    {
        List<OcrBox> page =
        [
            new("訂單編號", 360, 520, 75, 28), new("2610100TEST01", 363, 560, 126, 17), new("運送資訊", 362, 600, 70, 21),
            new("包裹1：新竹物流#5000000001", 363, 637, 211, 20), new("共1個商品", 416, 682, 78, 21), new("增加備註", 1310, 105, 60, 16),
            new("編", 358, 1170, 19, 17), new("單價", 1000, 1182, 36, 22), new("數量", 1066, 1182, 36, 22), new("小計", 1145, 1182, 35, 22),
            new("1", 362, 1300, 8, 12), new("120", 1010, 1280, 31, 18), new("2", 1098, 1282, 9, 14), new("240", 1150, 1280, 31, 18),
            new("商品選項貨號：X00009", 458, 1310, 126, 16), new("隱藏入帳明細", 1090, 1340, 80, 15), new("訂單金額", 950, 1370, 60, 18),
            new("買家實付金額", 360, 2000, 100, 22), new("訂單金額", 670, 2040, 60, 18), new("NT$240", 860, 2040, 55, 18),
            new("買家支付運費", 640, 2075, 90, 18), new("NT$0", 870, 2075, 36, 18), new("買家總支付金額", 630, 2160, 107, 18), new("NT$240", 830, 2150, 84, 26)
        ];
        Assert.True(ShopeePageScan.HasBuyerTotal(page));
        var problems = new List<string>();
        var sections = ShopeePageScan.Sections(page, problems);
        Assert.Empty(problems);
        Assert.Equal([ShopeeShotKind.Order, ShopeeShotKind.Items, ShopeeShotKind.Amounts], sections.Select(s => s.Kind));
        Assert.All(sections, s => Assert.Equal(s.Kind, ShopeeScreenshot.Classify(s.Lines)));
        Assert.DoesNotContain(sections[0].Lines, l => l.Text == "增加備註");
        Assert.DoesNotContain(sections[1].Lines, l => l.Text == "訂單金額");

        var (items, groups) = ShopeeScreenshot.ParseItems(sections[1].Lines, sections[1].PageArea.Width, problems);
        Assert.Empty(problems);
        Assert.Equal([new ShopeeShotItem(1, "X00009", 120, 2, 240)], items);
        Assert.Equal([new ShopeeShotGroup(1, 240, 240)], groups);
        Assert.Equal(240m, ShopeeScreenshot.ParseAmounts(sections[2].Lines, problems)!.Total);
    }

    [Fact]
    public void Never_takes_the_buyer_card_into_the_table()
    {
        // 隱藏入帳明細 not read (user's PC, 2026-10-13): the end must not fall through to the
        // 訂單金額 of the 買家實付金額 card, which made the table the amounts part.
        List<OcrBox> page =
        [
            new("訂單編號", 360, 520, 75, 28), new("2610100TEST01", 363, 560, 126, 17), new("共1個商品", 416, 682, 78, 21),
            new("編", 358, 1170, 19, 17), new("單價", 1000, 1182, 36, 22), new("數量", 1066, 1182, 36, 22), new("小計", 1145, 1182, 35, 22),
            new("1", 362, 1300, 8, 12), new("120", 1010, 1280, 31, 18), new("2", 1098, 1282, 9, 14), new("240", 1150, 1280, 31, 18),
            new("商品選項貨號：X00009", 458, 1310, 126, 16),
            new("買家實付金額", 360, 2000, 100, 22), new("訂單金額", 670, 2040, 60, 18), new("NT$240", 860, 2040, 55, 18),
            new("買家總支付金額", 630, 2160, 107, 18), new("NT$240", 830, 2150, 84, 26)
        ];
        var items = ShopeePageScan.Sections(page, []).Single(s => s.Kind == ShopeeShotKind.Items);
        Assert.DoesNotContain(items.Lines, l => l.Text.Contains("買家", StringComparison.Ordinal));
        Assert.Equal(ShopeeShotKind.Items, ShopeeScreenshot.Classify(items.Lines));
    }

    [Fact]
    public void Finds_the_table_when_a_header_glyph_is_missing()
    {
        // The PC read 「小計」 as 「小」 (diagnostic log, 2026-10-11); a stray icon digit sits far right.
        List<OcrBox> page =
        [
            new("編", 366, 1173, 14, 12), new("數量", 1075, 1184, 31, 19), new("商品", 399, 1185, 31, 19), new("單價", 1010, 1186, 28, 17),
            new("小", 1156, 1187, 20, 15), new("2", 369, 1301, 7, 12), new("700", 1014, 1299, 24, 16), new("1", 1098, 1302, 7, 9),
            new("700", 1159, 1299, 24, 16), new("商品選項貨號：X00012", 472, 1318, 124, 17), new("9", 1877, 1300, 22, 23),
            new("隱藏入帳明細", 1102, 1346, 73, 17)
        ];
        var problems = new List<string>();
        var items = ShopeePageScan.Sections(page, problems).Single(s => s.Kind == ShopeeShotKind.Items);
        Assert.DoesNotContain(problems, p => p.Contains("單價", StringComparison.Ordinal));
        Assert.Contains(items.Lines, l => l.Text == "700" && l.Left > 700);
        Assert.DoesNotContain(items.Lines, l => l.Text == "9");
    }

    [Fact]
    public void Says_which_part_is_missing()
    {
        var problems = new List<string>();
        ShopeePageScan.Sections([new OcrBox("訂單編號", 360, 520, 75, 28)], problems);
        Assert.Contains(problems, p => p.Contains("單價", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("買家實付金額", StringComparison.Ordinal));
    }
    [Fact]
    public void Splits_the_column_into_cards_on_grey_gaps()
    {
        // 20 grey rows, a 100-row card with a #FAFAFA table header (not grey), a 16-row gap,
        // a 10-row sliver (ignored), a gap, then a 60-row card running to the bottom.
        var shares = new List<double>();
        shares.AddRange(Enumerable.Repeat(1.0, 20));
        shares.AddRange(Enumerable.Repeat(0.08, 40));
        shares.AddRange(Enumerable.Repeat(0.0, 20));
        shares.AddRange(Enumerable.Repeat(0.08, 40));
        shares.AddRange(Enumerable.Repeat(0.97, 16));
        shares.AddRange(Enumerable.Repeat(0.1, 10));
        shares.AddRange(Enumerable.Repeat(1.0, 16));
        shares.AddRange(Enumerable.Repeat(0.05, 60));
        var cards = ShopeePageScan.Cards(shares);
        Assert.Equal([new ShopeePageScan.CardRange(20, 120), new ShopeePageScan.CardRange(162, 222)], cards);
    }

    [Theory]
    [InlineData("訂單編號 X00000000000AB", true)]
    [InlineData("進 帳 資 訊", true)]
    [InlineData("買家實付金額", true)]
    [InlineData("待出貨", false)]
    [InlineData("累計帳款調整", false)]
    [InlineData("買家訂單完成率", false)]
    public void Knows_which_cards_the_order_needs(string title, bool wanted) =>
        Assert.Equal(wanted, ShopeePageScan.WantedCard(title));
}
