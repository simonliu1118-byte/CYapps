using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>
/// Boxes as PP-OCRv5 read two sample orders (positions from the recognizer; order numbers,
/// item codes and tracking numbers replaced with fictitious ones).
/// </summary>
public class ShopeeScreenshotTests
{
    private static OcrBox B(string text, int x, int y, int w = 0, int h = 18) => new(text, x, y, w > 0 ? w : Math.Max(10, text.Length * 13), h);

    private static readonly ShopeeShotMapping Mapping = new("T01", "C0001", "蝦皮訂單", "D901", "S100", "P900",
        new Dictionary<string, string> { ["蝦皮店到店 - 隔日到貨"] = "F7", ["蝦皮店到店"] = "F6" });

    private static List<OcrBox> OrderShot() =>
    [
        B("訂單編號", 64, 50), B("2610000TEST01A", 65, 87), B("運送資訊", 64, 131),
        B("包裹1：蝦皮店到店-隔日到貨", 65, 173, 200), B("#TW200000000001K", 278, 173, 160), B("共1個商品", 124, 223)
    ];

    private static List<OcrBox> PromoItemShot() =>
    [
        B("編", 61, 25), B("商品", 99, 40), B("單價", 769, 39), B("數量", 842, 40), B("小計", 929, 39), B("號", 61, 57),
        B("Any 2 enjoy 1% off", 71, 113, 120), B("1", 68, 177, 8), B("【志遠】測試商品甲", 175, 149, 480), B("罐軟拔罐矽膠杯", 169, 167, 300),
        B("75", 791, 174, 18), B("5", 874, 175, 9), B("364", 933, 165, 26), B("375", 936, 183, 26),
        B("商品選項貨號：X00315-1", 169, 199, 180), B("隱藏入帳明細", 870, 229)
    ];

    private static List<OcrBox> PromoAmountShot() =>
    [
        B("買家實付金額", 79, 31), B("訂單金額", 752, 64), B("NT$364", 922, 65, 55), B("買家支付運費", 721, 99), B("NT$O", 937, 99, 40),
        B("優惠券", 767, 134), B("-NT$25", 924, 134, 55), B("賣場優惠券", 736, 169), B("-NTS12", 924, 168, 55),
        B("買家總支付金額", 706, 210), B("NT$327", 889, 207, 90, 26)
    ];

    private static ShopeeShotResult Read(List<OcrBox> order, List<List<OcrBox>> itemShots, List<OcrBox> amountShot)
    {
        var problems = new List<string>();
        var o = ShopeeScreenshot.ParseOrder(ShopeeScreenshot.MergeLines(order), problems);
        var parsed = itemShots.Select(s => ShopeeScreenshot.ParseItems(ShopeeScreenshot.MergeLines(s), 990, problems)).ToList();
        var a = ShopeeScreenshot.ParseAmounts(ShopeeScreenshot.MergeLines(amountShot), problems);
        return ShopeeScreenshot.Combine(o, parsed.SelectMany(p => p.Items).ToList(), parsed.SelectMany(p => p.Groups).ToList(), a, Mapping, problems);
    }

    [Fact]
    public void Classifies_the_three_screenshots()
    {
        Assert.Equal(ShopeeShotKind.Order, ShopeeScreenshot.Classify(ShopeeScreenshot.MergeLines(OrderShot())));
        Assert.Equal(ShopeeShotKind.Items, ShopeeScreenshot.Classify(ShopeeScreenshot.MergeLines(PromoItemShot())));
        Assert.Equal(ShopeeShotKind.Amounts, ShopeeScreenshot.Classify(ShopeeScreenshot.MergeLines(PromoAmountShot())));
    }

    [Fact]
    public void Builds_items_promotion_and_coupon_lines()
    {
        var result = Read(OrderShot(), [PromoItemShot()], PromoAmountShot());
        Assert.Empty(result.Problems);
        var order = Assert.IsType<ImportedOrder>(result.Order);
        Assert.Equal("2610000TEST01A", order.OrderSn);
        Assert.Equal("TW200000000001K", order.TrackingNumber);
        Assert.Equal(
        [
            new ImportedItem("X00315-1", "5", "75"),
            new ImportedItem("P900", "1", "-11"),
            new ImportedItem("D901", "1", "-37")
        ], order.Items);
        var header = order.HeaderValues();
        Assert.Equal("蝦皮訂單2610000TEST01A", header["trade_note"]);
        Assert.Equal("327", header["cod"]);
        Assert.Equal("F7", header["freight_type"]);
        Assert.Equal("TW200000000001K", header["ship_addr1"]);
        Assert.False(header.ContainsKey("freight_fee"));
    }

    [Fact]
    public void Shopee_coins_lower_the_collected_amount_without_a_line()
    {
        List<OcrBox> amounts =
        [
            B("訂單金額", 752, 64), B("NT$100", 922, 65, 55), B("蝦幣折抵", 740, 99), B("-NT$30", 924, 99, 55),
            B("買家總支付金額", 706, 140), B("NT$70", 889, 137, 60)
        ];
        List<OcrBox> items = [B("1", 68, 177, 8), B("100", 791, 174, 26), B("1", 874, 175, 9), B("100", 933, 175, 26), B("商品選項貨號：X00001", 169, 199, 160)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal([new ImportedItem("X00001", "1", "100")], result.Order!.Items);
        Assert.Equal("70", result.Order.HeaderValues()["cod"]);
    }

    [Fact]
    public void Shipping_becomes_a_line_and_the_freight_field()
    {
        List<OcrBox> amounts =
        [
            B("訂單金額", 752, 64), B("NT$100", 922, 65, 55), B("買家支付運費", 721, 99), B("NT$60", 937, 99, 40),
            B("買家總支付金額", 706, 140), B("NT$160", 889, 137, 60)
        ];
        List<OcrBox> items = [B("1", 68, 177, 8), B("100", 791, 174, 26), B("1", 874, 175, 9), B("100", 933, 175, 26), B("商品選項貨號：X00001", 169, 199, 160)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Contains(new ImportedItem("S100", "1", "60"), result.Order!.Items);
        Assert.Equal("60", result.Order.HeaderValues()["freight_fee"]);
        Assert.Equal("160", result.Order.HeaderValues()["cod"]);
    }

    [Fact]
    public void Merges_overlapping_detail_screenshots_and_reports_gaps()
    {
        List<OcrBox> first = [B("1", 68, 100, 8), B("50", 791, 100, 18), B("1", 874, 100, 9), B("50", 933, 100, 18), B("商品選項貨號：X00001", 169, 125, 160),
                              B("2", 68, 190, 8), B("30", 791, 190, 18), B("2", 874, 190, 9), B("60", 933, 190, 18), B("商品選項貨號：X00002", 169, 215, 160)];
        List<OcrBox> second = [B("2", 68, 40, 8), B("30", 791, 40, 18), B("2", 874, 40, 9), B("60", 933, 40, 18), B("商品選項貨號：X00002", 169, 65, 160),
                               B("4", 68, 130, 8), B("10", 791, 130, 18), B("1", 874, 130, 9), B("10", 933, 130, 18), B("商品選項貨號：X00004", 169, 155, 160)];
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$120", 922, 65, 55), B("買家總支付金額", 706, 140), B("NT$120", 889, 137, 60)];
        var result = Read(OrderShot(), [first, second], amounts);
        Assert.Contains(result.Problems, p => p.Contains("缺第 3 項"));
        Assert.Equal(3, result.Order!.Items.Count);
    }

    [Fact]
    public void Reports_amounts_that_do_not_add_up()
    {
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$364", 922, 65, 55), B("優惠券", 767, 134), B("-NT$25", 924, 134, 55),
                                B("買家總支付金額", 706, 210), B("NT$300", 889, 207, 90, 26)];
        var result = Read(OrderShot(), [PromoItemShot()], amounts);
        Assert.Contains(result.Problems, p => p.Contains("金額對不上"));
    }

    [Fact]
    public void A_promotion_group_with_a_set_code_splits_into_quantity_and_unit_price()
    {
        // One 編號 with three products and one 小計 171 (original 175); no header row on screen.
        List<OcrBox> items =
        [
            B("Any 2 enjoy 1% off", 41, 18, 120), B("【志遠】測試弧形杯", 145, 54, 480), B("40", 761, 80, 18), B("1", 844, 81, 9),
            B("規格: 6號", 138, 102, 150), B("商品项货號：X00014", 138, 122, 160),
            B("【志遠】測試弧形杯", 143, 140, 480), B("1", 37, 176, 8), B("規格:8號", 139, 189, 150), B("40", 761, 166, 18), B("1", 844, 167, 9),
            B("171", 903, 166, 26), B("175", 906, 183, 26), B("商品项货號：X00015", 138, 207, 160),
            B("【志遠】測試拔罐槍", 143, 225, 480), B("95", 760, 250, 18), B("1", 844, 251, 9),
            B("規格:優惠組合-拔罐植頭x2", 139, 274, 260), B("商品選項貨號：", 138, 292, 80), B("Y0033x2", 224, 291, 60), B("隱藏入帳明細", 840, 325)
        ];
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$171", 922, 65, 55), B("買家總支付金額", 706, 140), B("NT$171", 889, 137, 60)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal(
        [
            new ImportedItem("X00014", "1", "40"),
            new ImportedItem("X00015", "1", "40"),
            new ImportedItem("Y0033", "2", "47.5"),
            new ImportedItem("P900", "1", "-4")
        ], result.Order!.Items);
    }

    [Fact]
    public void A_set_price_that_does_not_divide_evenly_is_rounded_to_two_decimals()
    {
        List<OcrBox> items =
        [
            B("1", 49, 120, 8), B("119", 757, 120, 26), B("1", 850, 120, 9), B("119", 917, 120, 26),
            B("規格: 11x13cm (每包50片),3包優惠價", 159, 140, 240), B("商品選項貨號：X00579-1x3", 161, 160, 180)
        ];
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$119", 922, 65, 55), B("買家總支付金額", 706, 140), B("NT$119", 889, 137, 60)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal([new ImportedItem("X00579-1", "3", "39.67")], result.Order!.Items);
    }

    [Fact]
    public void Proposes_rereading_lone_digits_the_detector_missed()
    {
        // As the PC read it: the 編號 "1" and the 數量 "1" of the first row are missing.
        List<OcrBox> lines = ShopeeScreenshot.MergeLines(
        [
            B("編", 41, 18), B("商品", 80, 33), B("單價", 750, 33), B("數量", 823, 33), B("小計", 909, 32), B("號", 41, 50),
            B("150", 755, 127, 26), B("150", 915, 128, 26), B("商品選項貨號：X00701", 157, 162, 160),
            B("2", 45, 236, 8), B("60", 763, 235, 18), B("1", 847, 236, 9), B("60", 922, 235, 18), B("商品選項貨號：X00177", 157, 269, 160)
        ]);
        var probes = ShopeeScreenshot.MissingCellProbes(lines, 990, 300);
        Assert.Equal(2, probes.Count);
        var quantity = Assert.Single(probes, p => p.Purpose == "數量");
        Assert.InRange(quantity.Left + quantity.Width / 2, 830, 860);
        Assert.InRange(quantity.Top + quantity.Height / 2, 125, 145);
        var index = Assert.Single(probes, p => p.Purpose == "編號");
        Assert.InRange(index.Left + index.Width / 2, 35, 60);

        // With the re-read cells added the two products parse.
        lines.Add(new OcrBox("1", quantity.Left, quantity.Top, quantity.Width, quantity.Height));
        lines.Add(new OcrBox("1", index.Left, index.Top, index.Width, index.Height));
        var problems = new List<string>();
        var (items, groups) = ShopeeScreenshot.ParseItems(lines, 990, problems);
        Assert.Empty(problems);
        Assert.Equal(2, items.Count);
        Assert.Equal([1, 2], groups.Select(g => g.Index));
    }

    [Theory]
    [InlineData("1", true, "1")]
    [InlineData("I", true, "1")]
    [InlineData("l", true, "1")]
    [InlineData("O", true, "0")]
    [InlineData("12", true, "12")]
    [InlineData("A", false, "")]
    public void Maps_reread_look_alikes_to_digits(string text, bool ok, string expected)
    {
        Assert.Equal(ok, ShopeeScreenshot.TryProbeDigits(text, out var digits));
        if (ok) Assert.Equal(expected, digits);
    }

    [Theory]
    [InlineData("X00579-1x3", "X00579-1", 3)]
    [InlineData("Y0033x2", "Y0033", 2)]
    [InlineData("Y0033*2", "Y0033", 2)]
    [InlineData("Y0033 x 2", "Y0033", 2)]
    [InlineData("Y0033 X 3", "Y0033", 3)]
    [InlineData("X00315-1", "X00315-1", 1)]
    [InlineData("X00023", "X00023", 1)]
    public void Splits_set_codes(string text, string code, int multiplier)
    {
        Assert.Equal((code, (decimal)multiplier), ShopeeScreenshot.SplitSet(text));
    }

    [Fact]
    public void Coins_merged_with_their_note_and_a_split_simplified_code_label()
    {
        List<OcrBox> items =
        [
            B("編", 44, 34), B("單價", 753, 49), B("數量", 826, 49), B("小計", 912, 49), B("號", 45, 67),
            B("1", 49, 146, 8), B("249", 757, 144, 26), B("1", 850, 145, 9), B("249", 917, 144, 26),
            B("規格：4號-竟4.5公分（超取最多3包）", 159, 157, 220), B("商品選項货號：", 161, 178, 80), B("X00208", 246, 177, 60)
        ];
        List<OcrBox> amounts =
        [
            B("買家實付金額", 57, 29), B("訂單金額", 712, 61), B("NT$249", 899, 61, 55), B("買家支付運費", 682, 95), B("NTSO", 917, 96, 40),
            B("優惠券", 727, 130), B("-NT$18", 903, 131, 55), B("賣場優惠券", 697, 166), B("-NT$12", 903, 166, 55),
            B("蝦幣折抵", 712, 200), B("（⑤53蝦幣）-NT$53", 818, 200, 140), B("買家總支付金額", 666, 241), B("NT$166", 868, 238, 90, 26)
        ];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal([new ImportedItem("X00208", "1", "249"), new ImportedItem("D901", "1", "-30")], result.Order!.Items);
        Assert.Equal("166", result.Order.HeaderValues()["cod"]);
    }

    [Theory]
    [InlineData("NT$594", true, 594)]
    [InlineData("-NT$25", true, -25)]
    [InlineData("-NTS12", true, -12)]
    [InlineData("NT$O", true, 0)]
    [InlineData("NT$1,200", true, 1200)]
    [InlineData("（⑤53蝦幣）-NT$53", true, -53)]
    [InlineData("NTSO", true, 0)]
    [InlineData("訂單金額", false, 0)]
    public void Reads_ocr_amounts(string text, bool ok, decimal expected)
    {
        Assert.Equal(ok, ShopeeScreenshot.TryNumber(text, out var value, digitsOnly: false));
        if (ok) Assert.Equal(expected, value);
    }
}
