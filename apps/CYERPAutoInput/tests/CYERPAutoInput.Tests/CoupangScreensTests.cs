using Xunit;

namespace CYERPAutoInput.Tests;

public class CoupangScreensTests
{
    private static OcrBox B(string text, int left, int top, int width = 60, int height = 20) => new(text, left, top, width, height);

    // Laid out like the user's 正在配送 screenshot (2026-10-15); numbers made up.
    private static List<OcrBox> Page(string selected = "已選 0 筆", bool checkbox = true)
    {
        var boxes = new List<OcrBox>
        {
            B("配送中清單", 244, 397, 90), B("共 1 筆", 350, 400, 42), B(selected, 425, 400, 55), B("申請退貨", 500, 397, 60),
            B(" 下載Excel", 1640, 397, 90), B("顯示10個", 1750, 397, 80),
            B("訂單編號", 350, 476, 70), B("分箱出貨", 456, 476, 50), B("90000000000001", 322, 620, 110)
        };
        if (checkbox) boxes.Add(B(ShopeeBatchScreens.NamelessCheckBox, 263, 477, 18, 18));
        return boxes;
    }

    [Fact]
    public void Finds_download_header_and_select_all()
    {
        var page = Page();
        var download = CoupangScreens.DownloadExcel(page)!;
        Assert.Equal(1640, download.Left);
        var header = CoupangScreens.OrderHeader(page, download)!;
        Assert.Equal(476, header.Top);
        Assert.Equal(263, CoupangScreens.SelectAll(page, header).Left);
        // Without the check box in the page text: its place in the sample.
        var guessed = CoupangScreens.SelectAll(Page(checkbox: false), header);
        Assert.InRange(guessed.Left + guessed.Width / 2, 265, 280);
    }

    [Fact]
    public void Reads_total_and_selected()
    {
        Assert.Equal((1, 0), CoupangScreens.Counts(Page()));
        Assert.Equal((1, 1), CoupangScreens.Counts(Page("已選 1 筆")));
        Assert.Equal((null, null), CoupangScreens.Counts([B("訂單編號", 350, 476)]));
    }
}
