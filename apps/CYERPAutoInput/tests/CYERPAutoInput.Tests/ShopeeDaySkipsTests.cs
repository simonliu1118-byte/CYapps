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
