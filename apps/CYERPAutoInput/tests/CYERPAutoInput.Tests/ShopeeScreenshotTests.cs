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
        var items = itemShots.SelectMany(s => ShopeeScreenshot.ParseItems(ShopeeScreenshot.MergeLines(s), 990, problems)).ToList();
        var a = ShopeeScreenshot.ParseAmounts(ShopeeScreenshot.MergeLines(amountShot), problems);
        return ShopeeScreenshot.Combine(o, items, a, Mapping, problems);
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

    [Theory]
    [InlineData("NT$594", true, 594)]
    [InlineData("-NT$25", true, -25)]
    [InlineData("-NTS12", true, -12)]
    [InlineData("NT$O", true, 0)]
    [InlineData("NT$1,200", true, 1200)]
    [InlineData("訂單金額", false, 0)]
    public void Reads_ocr_amounts(string text, bool ok, decimal expected)
    {
        Assert.Equal(ok, ShopeeScreenshot.TryNumber(text, out var value, digitsOnly: false));
        if (ok) Assert.Equal(expected, value);
    }
}
