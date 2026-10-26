using CYERPAutoInput;
using Xunit;

namespace CYERPAutoInput.Tests;

public sealed class ShopeeDaySkipsTests
{
    private static OcrBox B(string text, int left, int top, int width = 100, int height = 20) => new(text, left, top, width, height);

    [Fact]
    public void Store_closed_toast_is_read()
    {
        List<OcrBox> page =
        [
            B("待出貨", 300, 127, 53), B("為了避免延遲出貨，請在 2026/10/27 之前 出貨 。", 300, 165, 330),
            B("買家門市關轉，請待買家重選完門市後，再重新申請寄件編號。", 710, 88, 485), B("(error:10514)", 905, 109, 102),
            B("不成立訂單", 976, 231, 113), B("安排出貨", 1099, 231, 99)
        ];
        var error = ShopeeBatchScreens.ShipError(page);
        Assert.Equal("買家門市關轉，請待買家重選完門市後，再重新申請寄件編號。(error:10514)", error);
        Assert.True(ShopeeBatchScreens.StoreClosed(error!));
        Assert.Null(ShopeeBatchScreens.ShipError(page.Where(b => !b.Text.Contains("error")).ToList()));
        Assert.False(ShopeeBatchScreens.StoreClosed("系統忙碌(error:500)"));
    }

    [Fact]
    public void Buyer_choice_spec_is_found()
    {
        List<OcrBox> page =
        [
            B("【志遠】§台灣製現貨§ 按摩精油膏 450ml大罐 綠薰舒 紫薰舒蘆薈玫瑰香茅薄荷生薑 推拿整復", 107, 90, 477),
            B("規格: 任選3罐(下單請備註指定的味道)", 107, 138, 222)
        ];
        Assert.Equal("規格:任選3罐(下單請備註指定的味道)", ShopeeBatchScreens.BuyerChoiceSpec(page));
        Assert.Null(ShopeeBatchScreens.BuyerChoiceSpec([B("規格: 兩支優惠組(長刷x1+丸形刷x1)", 107, 138), B("買家備註 任選", 0, 0)]));
    }

    [Fact]
    public void Black_cat_is_a_pickup()
    {
        Assert.Equal(ShopeeBatchCarrier.BlackCat, ShopeeBatchScreens.CarrierKind("黑貓宅急便"));
        Assert.True(ShopeeBatchScreens.IsPickup(ShopeeBatchCarrier.BlackCat));
        Assert.True(ShopeeBatchScreens.IsPickup(ShopeeBatchCarrier.Hct));
        Assert.False(ShopeeBatchScreens.IsPickup(ShopeeBatchCarrier.Shopee));
        // 快遞取件詳情 shows the number in groups of three.
        Assert.Equal("626152397515", ShopeeBatchScreens.DialogTracking([B("快遞取件詳情", 80, 115, 130), B("626 152 397 515", 339, 176, 150)]));
    }

    [Fact]
    public void Ink_bounds_box_the_printed_part()
    {
        var page = GrayImage.Blank(50, 40);
        page.Pixels[5 * 50 + 10] = 0;
        page.Pixels[20 * 50 + 30] = 0;
        Assert.Equal(System.Drawing.Rectangle.FromLTRB(10, 5, 31, 21), SlipImage.InkBounds(page));
        Assert.Null(SlipImage.InkBounds(GrayImage.Blank(5, 5)));
    }

    [Fact]
    public void Skips_last_for_the_day()
    {
        var today = new DateTime(2026, 10, 26);
        var json = ShopeeDaySkips.With(null, today, "261023DCFDQ7V7", "買家門市關轉");
        json = ShopeeDaySkips.With(json, today, "261023AAAAAAAA", "買家門市關轉");
        Assert.Equal(2, ShopeeDaySkips.Today(json, today).Count);
        Assert.Empty(ShopeeDaySkips.Today(json, today.AddDays(1)));
        // Yesterday's list is dropped when today's first is added.
        Assert.Single(ShopeeDaySkips.Today(ShopeeDaySkips.With(json, today.AddDays(1), "X", "r"), today.AddDays(1)));
        Assert.Empty(ShopeeDaySkips.Today("not json", today));
    }
}
