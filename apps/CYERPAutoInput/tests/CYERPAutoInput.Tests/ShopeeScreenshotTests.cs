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
    public void A_gift_part_goes_to_the_gift_quantity_and_the_rest_sells_alone()
    {
        // 「A00175+A00173(贈)」 (user's screenshot, 2026-10-20; codes replaced).
        var items = PromoItemShot().Select(b => b.Text == "商品選項貨號：X00315-1" ? b with { Text = "商品選項貨號：X00315-1+X00173(贈)", Width = 260 } : b).ToList();
        var result = Read(OrderShot(), [items], PromoAmountShot());
        Assert.Empty(result.Problems);
        var order = Assert.IsType<ImportedOrder>(result.Order);
        Assert.Equal(
        [
            new ImportedItem("X00315-1", "5", "75"),
            new ImportedItem("X00173", "0", "0") { GiftQuantity = "5" },
            new ImportedItem("P900", "1", "-11"),
            new ImportedItem("D901", "1", "-37")
        ], order.Items);
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

    // As the PC read it (2026-10-11): no header row and the 數量 "1" missed, so only the
    // 單價 and 小計 columns have numbers; the 小計 used to be taken as the quantity.
    private static List<OcrBox> NoHeaderMissingQuantity() =>
    [
        B("1", 43, 57, 7, 13), B("450", 752, 56, 28, 15), B("450", 912, 56, 27, 15),
        B("商品選項貨號：X00086", 157, 90, 160)
    ];

    [Fact]
    public void Does_not_take_the_subtotal_for_a_missed_quantity()
    {
        var lines = ShopeeScreenshot.MergeLines(NoHeaderMissingQuantity());
        var probe = Assert.Single(ShopeeScreenshot.MissingCellProbes(lines, 990, 160));
        Assert.Equal("數量", probe.Purpose);
        Assert.InRange(probe.Left + probe.Width / 2, 820, 870);

        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$450", 922, 65, 55), B("買家總支付金額", 706, 140), B("NT$450", 889, 137, 60)];
        var result = Read(OrderShot(), [NoHeaderMissingQuantity()], amounts);
        Assert.Equal([new ImportedItem("X00086", "1", "450")], result.Order!.Items);
        Assert.Contains(result.Problems, p => p.Contains("已依小計推算為 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Reads_the_quantity_column_once_the_reread_finds_it()
    {
        var lines = ShopeeScreenshot.MergeLines(NoHeaderMissingQuantity());
        lines.Add(new OcrBox("1", 844, 57, 7, 13));
        var problems = new List<string>();
        var (items, groups) = ShopeeScreenshot.ParseItems(lines, 990, problems);
        Assert.Empty(problems);
        Assert.Equal([new ShopeeShotItem(1, "X00086", 450, 1, 450)], items);
        Assert.Equal([new ShopeeShotGroup(1, 450, 450)], groups);
    }

    [Fact]
    public void Ignores_digits_read_from_product_thumbnails()
    {
        // As the PC read it (2026-10-11): a thumbnail's small print 「4號杯」 read as "4" beside
        // the 編號 column made up a 第 4 項 with no 小計.
        List<OcrBox> items =
        [
            B("Any 2 enjoy 1% off", 63, 29, 124), B("100", 775, 91, 32), B("1", 868, 94, 10, 13),
            B("商品選項貨號：X00145", 162, 134, 137), B("213", 922, 134, 37), B("1", 59, 146, 11, 14), B("220", 927, 151, 31),
            B("30", 784, 178, 22), B("4", 867, 180, 11, 13), B("4", 112, 200, 9, 12), B("商品選項貨號：X00003", 161, 219, 137),
            B("Any 2 enjoy 1% off", 62, 271, 125), B("30", 783, 336, 23), B("4", 867, 338, 11, 13),
            B("商品選項貨號：X00002", 161, 375, 137), B("145", 924, 376, 35), B("2", 57, 387, 14), B("150", 928, 395, 30),
            B("30", 784, 421, 22), B("1", 867, 422, 11, 15), B("商品選項貨號：X00003", 161, 460, 138),
            B("3", 57, 539, 13), B("30", 775, 538, 23), B("1", 860, 540, 9, 13), B("30", 934, 538, 24), B("商品選項貨號：X00002", 170, 571, 136)
        ];
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$388", 922, 65, 55), B("買家總支付金額", 706, 140), B("NT$388", 889, 137, 60)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal(
            [new ImportedItem("X00145", "1", "100"), new ImportedItem("X00003", "5", "30"), new ImportedItem("X00002", "5", "30"), new ImportedItem("P900", "1", "-12")],
            result.Order!.Items);
    }

    [Fact]
    public void Takes_the_products_sum_when_the_struck_original_was_missed()
    {
        // As the PC read it (2026-10-11): group 1 shows 2,716 but its struck 2,800 was not read.
        List<OcrBox> items =
        [
            B("Any 2 enjoy 1% off", 63, 29, 124), B("600", 772, 85, 32), B("3", 864, 87, 10, 13),
            B("商品選項貨號：X00154", 160, 108, 137), B("1", 59, 130, 11, 14), B("2,716", 908, 122, 40),
            B("500", 772, 150, 32), B("2", 864, 152, 10, 13), B("商品選項貨號：X00376", 160, 207, 137),
            B("Any 2 enjoy 1% off", 63, 248, 124), B("2", 57, 310, 14), B("500", 772, 310, 32), B("2", 864, 312, 10, 13),
            B("990", 920, 307, 30), B("1,000", 912, 326, 40), B("商品選項貨號：X00417", 160, 352, 137)
        ];
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$3706", 922, 65, 70), B("買家總支付金額", 706, 140), B("NT$3706", 889, 137, 70)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal(
            [new ImportedItem("X00154", "3", "600"), new ImportedItem("X00376", "2", "500"), new ImportedItem("X00417", "2", "500"), new ImportedItem("P900", "1", "-94")],
            result.Order!.Items);
    }

    [Fact]
    public void Ignores_stray_digits_in_the_index_column()
    {
        // Header present; a stray "5" read above the first row (the 號 of 編號) is no 編號.
        List<OcrBox> items =
        [
            B("編", 80, 15, 18), B("單價", 790, 40, 34), B("數量", 860, 40, 34), B("小計", 945, 40, 34), B("5", 82, 60, 10, 14),
            B("1", 84, 125, 9, 13), B("200", 790, 125, 30), B("1", 880, 125, 9, 13), B("200", 960, 125, 30), B("商品選項貨號：X00234", 197, 160, 137),
            B("2", 84, 232, 10, 13), B("150", 790, 232, 30), B("1", 880, 232, 9, 13), B("150", 960, 232, 30), B("商品選項貨號：X00292", 197, 267, 137),
            B("3", 84, 340, 10, 13), B("100", 790, 340, 30), B("1", 880, 340, 9, 13), B("100", 960, 340, 30), B("商品選項貨號：X00151", 197, 375, 137)
        ];
        var problems = new List<string>();
        var (_, groups) = ShopeeScreenshot.ParseItems(ShopeeScreenshot.MergeLines(items), 1040, problems);
        Assert.Empty(problems);
        Assert.Equal([1, 2, 3], groups.Select(g => g.Index));
    }

    [Fact]
    public void Reads_misread_index_glyphs_and_skips_thumbnail_print()
    {
        // The PC's boxes (diagnostic log, 2026-10-11): 編號 "1" read as "-", "2" as "乙", and a
        // thumbnail's 「5號杯」 read as "5" beside group 2.
        List<OcrBox> items =
        [
            B("Any2enjoy1%off", 63, 23, 111, 19), B("30", 777, 85, 16, 17), B("1", 861, 87, 7, 12),
            B("5號择", 104, 114, 15, 6), B("商品選項貨號：X00005", 157, 128, 132, 15), B("145", 919, 128, 28, 19),
            B("-", 54, 140, 7, 11), B("150", 923, 146, 21, 17), B("30", 781, 171, 11, 17), B("4", 861, 173, 8, 12),
            B("G號", 104, 199, 11, 6), B("商品選項貨號：X00006", 157, 213, 131, 16), B("Any2enjoy1%off", 60, 266, 115, 19),
            B("30", 777, 329, 19, 16), B("59", 929, 329, 17, 17), B("2", 860, 330, 9, 14), B("乙", 52, 339, 10, 13),
            B("60", 934, 348, 9, 13), B("5", 103, 355, 11, 9), B("商品選項貨號：X00005", 155, 370, 133, 17)
        ];
        List<OcrBox> amounts = [B("訂單金額", 752, 64), B("NT$204", 922, 65, 55), B("買家總支付金額", 706, 140), B("NT$204", 889, 137, 60)];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        Assert.Equal(
            [new ImportedItem("X00005", "3", "30"), new ImportedItem("X00006", "4", "30"), new ImportedItem("P900", "1", "-6")],
            result.Order!.Items);
    }

    [Fact]
    public void Reads_look_alike_glyphs_in_the_quantity_column()
    {
        // The PC's boxes (diagnostic log, 2026-10-11): 編號 and 數量 "1" both read as "一".
        List<OcrBox> lines =
        [
            B("350", 780, 54, 24, 16), B("350", 937, 54, 27, 16), B("一", 69, 56, 8, 11), B("一", 870, 56, 8, 11),
            B("規格:測試", 180, 68, 143, 17), B("商品選項貨號：X00206", 181, 88, 134, 15)
        ];
        Assert.Empty(ShopeeScreenshot.MissingCellProbes(lines, 1036, 139));
        var problems = new List<string>();
        var (items, groups) = ShopeeScreenshot.ParseItems(lines, 1036, problems);
        Assert.Empty(problems);
        Assert.Equal([new ShopeeShotItem(1, "X00206", 350, 1, 350)], items);
        Assert.Equal([new ShopeeShotGroup(1, 350, 350)], groups);
    }

    [Fact]
    public void Numbers_guessed_index_glyphs_from_their_neighbours()
    {
        // The PC's boxes (diagnostic log, 2026-10-11): 編號 1 and 2 both read as "-", 3 and 4 clean.
        List<OcrBox> items =
        [
            B("30", 771, 86, 11, 17), B("5", 850, 87, 9, 14), B("146", 909, 87, 26, 18), B("-", 45, 99, 4, 7), B("150", 915, 105, 19, 15),
            B("4號", 95, 114, 9, 7), B("商品選項貨號:X00004", 147, 129, 132, 15),
            B("30", 768, 244, 15, 16), B("146", 909, 244, 27, 18), B("5", 850, 245, 8, 14), B("-", 44, 257, 7, 9), B("150", 916, 263, 19, 15),
            B("2號", 94, 272, 11, 7), B("商品選項貨號：X00003", 146, 286, 133, 18),
            B("30", 771, 402, 11, 17), B("146", 909, 402, 27, 18), B("5", 850, 403, 9, 14), B("3", 42, 413, 9, 14), B("150", 915, 420, 20, 16),
            B("2號", 93, 430, 13, 8), B("商品選項貨號:X00002", 146, 444, 134, 16),
            B("30", 769, 560, 16, 16), B("146", 909, 560, 26, 18), B("5", 850, 561, 9, 15), B("4", 43, 571, 9, 12), B("150", 915, 578, 19, 16),
            B("商品選項貨號:X00001", 146, 602, 132, 15)
        ];
        var problems = new List<string>();
        var (parsed, groups) = ShopeeScreenshot.ParseItems(ShopeeScreenshot.MergeLines(items), 983, problems);
        Assert.Empty(problems);
        Assert.Equal([1, 2, 3, 4], groups.Select(g => g.Index));
        Assert.Equal([1, 2, 3, 4], parsed.Select(i => i.Group));
    }

    [Fact]
    public void Treats_an_original_below_the_price_as_misread()
    {
        // The PC's boxes (diagnostic log, 2026-10-11): group 2's struck 480 read as 180.
        List<OcrBox> items =
        [
            B("96", 777, 93, 11, 16), B("466", 917, 93, 23, 19), B("5", 857, 94, 8, 14), B("1", 50, 106, 6, 10), B("480", 921, 113, 21, 13),
            B("商品選項貨號：X00470", 152, 136, 132, 16),
            B("96", 777, 250, 12, 17), B("466", 918, 251, 22, 18), B("5", 857, 252, 8, 14), B("2", 50, 264, 6, 10), B("180", 924, 271, 18, 12),
            B("商品選項貨號:X00470", 151, 293, 134, 17)
        ];
        var problems = new List<string>();
        var (_, groups) = ShopeeScreenshot.ParseItems(items, 979, problems);
        Assert.Empty(problems);
        Assert.Equal([new ShopeeShotGroup(1, 466, 480), new ShopeeShotGroup(2, 466, 480)], groups);
    }

    [Theory]
    [InlineData("包裹1:新竹物流#5000000001")]
    [InlineData("包裏1:新竹物流#5000000001")]
    [InlineData("包里1:新竹物流#5000000001")]
    public void Reads_the_carrier_whatever_glyph_follows_the_package_word(string line)
    {
        var problems = new List<string>();
        var order = ShopeeScreenshot.ParseOrder(ShopeeScreenshot.MergeLines(
            [B("訂單編號", 64, 50), B("2610000TEST01A", 65, 87), B("運送資訊", 64, 131), B(line, 65, 173, 290)]), problems);
        Assert.Equal("新竹物流", order.Carrier);
        Assert.Equal("5000000001", order.TrackingNumber);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("2610000TEST01A", false)]
    [InlineData("2610000TESST01A", true)]
    [InlineData("26100TEST01AB", true)]
    public void Flags_an_order_number_that_breaks_the_shopee_format(string orderSn, bool flagged)
    {
        // The PC once read 「QV」 as 「QWW」, giving 15 characters (2026-10-11).
        var problems = new List<string>();
        var order = ShopeeScreenshot.ParseOrder(ShopeeScreenshot.MergeLines(
            [B("訂單編號", 64, 50), B(orderSn, 65, 87), B("運送資訊", 64, 131), B("包裹1：新竹物流#5000000001", 65, 173, 290)]), problems);
        Assert.Equal(orderSn, order.OrderSn);
        Assert.Equal(flagged, problems.Any(p => p.Contains("14 碼", StringComparison.Ordinal)));
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
    [Fact]
    public void Reads_amount_labels_the_ocr_returned_in_simplified_glyphs()
    {
        // The PC read 「訂單金額」 as 「訂單金额」 (diagnostic log, 2026-10-12).
        List<OcrBox> shot =
        [
            new("買家實付金額", 40, 12, 126, 19), new("訂單金额", 682, 40, 56, 18), new("NT$1,000", 830, 42, 57, 15),
            new("買家支付運費", 657, 74, 81, 17), new("NT$0", 856, 73, 32, 18), new("賣場優惠券", 670, 137, 70, 17),
            new("-NT$40", 840, 137, 50, 19), new("买家總支付金額", 644, 174, 96, 16), new("NT$960", 794, 169, 96, 24)
        ];
        var problems = new List<string>();
        var amounts = ShopeeScreenshot.ParseAmounts(shot, problems)!;
        Assert.Empty(problems);
        Assert.Equal(1000m, amounts.OrderAmount);
        Assert.Equal(960m, amounts.Total);
    }
    [Fact]
    public void A_one_product_table_without_its_index_is_still_one_group()
    {
        // The lone 「1」 was not read (user's PC, 2026-10-12): 75 × 5 = 375 struck, 364 shown.
        var items = PromoItemShot().Where(b => b.Text != "1").ToList();
        var result = Read(OrderShot(), [items], PromoAmountShot());
        Assert.DoesNotContain(result.Problems, p => p.Contains("促銷組合", StringComparison.Ordinal));
        Assert.Contains(result.Order!.Items, i => i.ItemCode == "P900" && i.UnitPrice == "-11");
    }
    // Ink-run widths as measured on the user's screenshots (2026-10-12); the order number's
    // letters are replaced with fictitious ones of the same widths.
    private static readonly int[] OrderSnRuns = [7, 7, 3, 7, 7, 6, 7, 11, 15, 11, 7, 6, 7];

    [Theory]
    [InlineData("2610096WSAWP90", "2610096WSAWP90")]
    [InlineData("2610096WSAWP9O", "2610096WSAWP90")]
    public void A_glyph_as_wide_as_the_digits_is_a_zero(string read, string expected) =>
        Assert.Equal(expected, ShopeeScreenshot.ResolveZeroO(read, OrderSnRuns).Text);

    [Fact]
    public void A_glyph_a_third_wider_than_the_digits_is_an_o()
    {
        int[] runs = [.. OrderSnRuns[..^1], 10];
        var (text, decisions) = ShopeeScreenshot.ResolveZeroO("2610096WSAWP90", runs);
        Assert.Equal("2610096WSAWP9O", text);
        Assert.Single(decisions);
    }

    [Fact]
    public void Leaves_the_reading_when_the_glyphs_cannot_be_lined_up()
    {
        Assert.Equal("2610096WSAWP90", ShopeeScreenshot.ResolveZeroO("2610096WSAWP90", [40, 50, 30]).Text);
        Assert.Equal("2610096WSAWP90", ShopeeScreenshot.ResolveZeroO("2610096WSAWP90", []).Text);
    }

    [Theory]
    [InlineData("261O1O9R5EC3RC", "2610109R5EC3RC")]
    [InlineData("2610109R5EC3RC", "2610109R5EC3RC")]
    [InlineData("26I0I09R5EC3RC", "2610109R5EC3RC")]
    public void The_date_part_of_an_order_number_is_digits(string read, string expected) =>
        Assert.Equal(expected, ShopeeScreenshot.OrderSnDateDigits(read));

    [Theory]
    [InlineData("TW2650798798O9K", "TW265079879809K")]
    [InlineData("TW26507987985O0", "TW265079879850" + "0")]
    [InlineData("5O6O5O6113", "5060506113")]
    [InlineData("AB12O34567", "AB12O34567")]
    [InlineData("TW2661882I1759I", "TW266188211759I")]
    [InlineData("5O6O5I6113", "5060516113")]
    public void Tracking_digits_follow_the_carrier_format(string read, string expected) =>
        Assert.Equal(expected, ShopeeScreenshot.TrackingDigits(read));

    [Fact]
    public void The_order_screenshot_takes_the_date_digits_and_tracking_format()
    {
        var problems = new List<string>();
        var order = ShopeeScreenshot.ParseOrder(ShopeeScreenshot.MergeLines(
        [
            B("訂單編號", 64, 50), B("26100O0TEST01A", 65, 87), B("運送資訊", 64, 131),
            B("包裹1：蝦皮店到店", 65, 173, 200), B("#TW2000000O0001K", 278, 173, 160)
        ]), problems);
        Assert.Equal("2610000TEST01A", order.OrderSn);
        Assert.Equal("TW200000000001K", order.TrackingNumber);
    }
    [Theory]
    [InlineData(234)] // close enough to be merged into one box
    [InlineData(260)] // two separate boxes, as on the user's PC
    public void Reads_the_code_when_the_label_lost_its_first_glyphs(int codeLeft)
    {
        // 「商品選項貨號：」 read as 「商品選項」 and 「號：…」 (user's PC, 2026-10-12).
        var items = PromoItemShot()
            .Where(b => !b.Text.StartsWith("商品選項", StringComparison.Ordinal))
            .Concat([B("商品選項", 169, 199, 49), B("號：X00315-1", codeLeft, 199, 66)])
            .ToList();
        var result = Read(OrderShot(), [items], PromoAmountShot());
        Assert.Empty(result.Problems);
        Assert.Contains(result.Order!.Items, i => i.ItemCode == "X00315-1");
    }
    [Fact]
    public void Names_the_one_quantity_that_would_make_a_group_add_up_without_changing_it()
    {
        // Positions from the user's log (2026-10-12), codes replaced: the 2nd product's 「2」 was read as 「7」.
        List<OcrBox> lines =
        [
            new("單價", 744, 30, 28, 17), new("數量", 816, 30, 32, 17), new("小計", 905, 32, 28, 15),
            new("40", 765, 515, 15, 15), new("2", 847, 516, 7, 13), new("商品選項貨號：X00298", 140, 554, 137, 18),
            new("302", 904, 589, 29, 20), new("3", 37, 599, 13, 17), new("40", 767, 599, 11, 17), new("7", 847, 599, 9, 12),
            new("310", 910, 608, 20, 16), new("商品選項貨號：X00297", 143, 641, 131, 17),
            new("150", 756, 685, 25, 17), new("1", 848, 688, 7, 9), new("商品選項貨號：X00314", 142, 708, 133, 16)
        ];
        var problems = new List<string>();
        var (items, _) = ShopeeScreenshot.ParseItems(lines, 973, problems);
        Assert.Equal(7m, items.Single(i => i.ItemCode == "X00297").Quantity);
        Assert.Contains(problems, p => p.Contains("X00297", StringComparison.Ordinal) && p.Contains("若是 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Names_no_quantity_when_more_than_one_could_be_wrong()
    {
        List<OcrBox> lines =
        [
            new("單價", 744, 30, 28, 17), new("數量", 816, 30, 32, 17), new("小計", 905, 32, 28, 15),
            new("40", 765, 515, 15, 15), new("2", 847, 516, 7, 13), new("商品選項貨號：X00298", 140, 554, 137, 18),
            new("200", 904, 589, 29, 20), new("1", 37, 599, 8, 17), new("40", 767, 599, 11, 17), new("2", 847, 599, 9, 12),
            new("商品選項貨號：X00297", 143, 641, 131, 17)
        ];
        // 40×2 + 40×2 ≠ 200, and either product at 3 would add up: no way to tell which.
        var problems = new List<string>();
        var (items, groups) = ShopeeScreenshot.ParseItems(lines, 973, problems);
        Assert.Equal(200m, groups.Single().Original);
        Assert.DoesNotContain(problems, p => p.Contains("若是", StringComparison.Ordinal));
        Assert.All(items, i => Assert.Equal(2m, i.Quantity));
    }
    // Boxes from the user's log (2026-10-12; codes replaced): 編號 「1」 was re-read at 0.29 and dropped.
    private static List<OcrBox> ThreeGroupsWithoutIndexOne() =>
    [
        new("商品", 72, 28, 30, 17), new("單價", 744, 30, 27, 14), new("數量", 818, 30, 26, 14), new("計", 915, 32, 14, 12),
        new("96", 765, 162, 11, 17), new("1", 847, 165, 6, 10), new("商品選項貨號：X00641S", 140, 185, 140, 20),
        new("329", 902, 194, 27, 21), new("336", 915, 217, 15, 10), new("120", 755, 228, 23, 18), new("2", 844, 229, 13, 16),
        new("商品選項貨號：X00465", 140, 270, 133, 18),
        new("96", 757, 337, 13, 19), new("96", 917, 338, 11, 17), new("1", 838, 341, 9, 11), new("商品項貨號：X00641S", 147, 360, 145, 19),
        new("96", 765, 475, 13, 19), new("190", 905, 476, 24, 19), new("2", 845, 478, 11, 14), new("3", 36, 487, 11, 14),
        new("商品選項貨號：X00469", 141, 516, 134, 19), new("2", 16, 329, 50, 34)
    ];

    [Fact]
    public void The_numbering_implies_the_missing_first_index()
    {
        Assert.Equal(1, ShopeeScreenshot.ExpectedIndexAt(ThreeGroupsWithoutIndexOne(), 969, 204));
        Assert.Null(ShopeeScreenshot.ExpectedIndexAt(ThreeGroupsWithoutIndexOne(), 969, 420)); // between 2 and 3
    }

    [Fact]
    public void With_the_implied_first_index_all_three_groups_add_up()
    {
        var lines = ThreeGroupsWithoutIndexOne().Append(new OcrBox("1", 16, 187, 50, 34)).ToList();
        var problems = new List<string>();
        var (items, groups) = ShopeeScreenshot.ParseItems(lines, 969, problems);
        Assert.Equal([1, 2, 3], groups.Select(g => g.Index).Order());
        Assert.Equal(groups.Sum(g => g.Original), items.Sum(i => i.Amount));
        Assert.DoesNotContain(problems, p => p.Contains("缺第", StringComparison.Ordinal));
    }
    [Fact]
    public void A_bundle_enters_every_part_with_the_price_on_the_first()
    {
        // 「X00244+X00011」 × 2 at 400, struck 800 shown 792 (user's screenshot, 2026-10-12; codes replaced).
        List<OcrBox> lines =
        [
            new("單價", 744, 30, 28, 17), new("數量", 816, 30, 32, 17), new("小計", 905, 32, 28, 15),
            new("1", 37, 150, 8, 14), new("400", 745, 140, 26, 17), new("2", 828, 140, 8, 14), new("792", 906, 140, 26, 17),
            new("800", 906, 158, 26, 14), new("商品選項貨號：X00244+X00011", 180, 178, 190, 17)
        ];
        var problems = new List<string>();
        var (items, groups) = ShopeeScreenshot.ParseItems(lines, 1020, problems);
        Assert.Empty(problems);
        Assert.Equal(2, items.Count);
        Assert.Equal(("X00244", 400m, 2m), (items[0].ItemCode, items[0].UnitPrice, items[0].Quantity));
        Assert.Equal(("X00011", 0m, 2m), (items[1].ItemCode, items[1].UnitPrice, items[1].Quantity));
        Assert.Equal(groups.Single().Original, items.Sum(i => i.Amount));
    }

    [Theory]
    [InlineData("X00244+X00011", new[] { "X00244", "X00011" })]
    [InlineData("X00244十X00011", new[] { "X00244", "X00011" })]
    [InlineData("X00244 ＋ X00011x2", new[] { "X00244", "X00011x2" })]
    [InlineData("X00244", new[] { "X00244" })]
    public void Splits_bundle_codes(string raw, string[] parts) =>
        Assert.Equal(parts, ShopeeScreenshot.BundleParts(raw));
    [Fact]
    public void A_bundle_leaves_its_prices_to_erp_and_the_promotion_takes_the_gap()
    {
        List<OcrBox> items =
        [
            new("單價", 744, 30, 28, 17), new("數量", 816, 30, 32, 17), new("小計", 905, 32, 28, 15),
            new("1", 37, 150, 8, 14), new("400", 745, 140, 26, 17), new("2", 828, 140, 8, 14), new("792", 906, 140, 26, 17),
            new("800", 906, 158, 26, 14), new("商品選項貨號：X00244+X00011", 180, 178, 190, 17)
        ];
        List<OcrBox> amounts =
        [
            B("買家實付金額", 79, 31), B("訂單金額", 752, 64), B("NT$792", 922, 65, 55), B("買家總支付金額", 706, 210), B("NT$792", 889, 207, 90, 26)
        ];
        var result = Read(OrderShot(), [items], amounts);
        Assert.Empty(result.Problems);
        var lines = result.Order!.Items;
        var first = lines.Single(i => i.ItemCode == "X00244");
        var second = lines.Single(i => i.ItemCode == "X00011");
        Assert.Equal(("2", "", "800"), (first.Quantity, first.UnitPrice, first.BundleTotal));
        Assert.Equal(("2", "", (string?)null), (second.Quantity, second.UnitPrice, second.BundleTotal));
        var promotion = lines.Single(i => i.ItemCode == "P900");
        Assert.Equal("-8", promotion.UnitPrice);
        Assert.True(promotion.TakesBundleGap);
    }
}
