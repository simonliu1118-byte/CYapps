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

    // Laid out like the user's 訂單確認 screenshots (2026-10-19); order numbers made up.
    private static List<OcrBox> ConfirmPage() =>
    [
        B("訂單確認", 271, 335, 56), B("2", 524, 325, 20, 34), B("配送準備", 597, 335, 56), B("0", 849, 325, 20, 34),
        B("配送指示", 922, 335, 56), B("0", 1175, 325, 20, 34),
        B("待確認訂單清單", 250, 528, 126), B("共 2 筆", 392, 530, 42), B("已選 0 筆", 467, 530, 55),
        B("訂單編號", 356, 753, 52), B("分箱出貨", 462, 753, 50), B(ShopeeBatchScreens.NamelessCheckBox, 271, 752, 18, 18),
        B("900000000000001", 329, 876, 106), B("900000000000002", 329, 1040, 106),
        B("點擊確認訂單後，其他狀態會變更為待安排配送", 1409, 1000, 294), B("確認訂單", 1772, 998, 72, 24)
    ];

    [Fact]
    public void Reads_status_cards_list_and_confirm_button()
    {
        var page = ConfirmPage();
        var (label, count) = CoupangScreens.StatusCard(page, "訂單確認")!.Value;
        Assert.Equal((271, 2), (label.Left, count));
        Assert.Equal(0, CoupangScreens.StatusCard(page, "配送準備")!.Value.Count);
        var header = CoupangScreens.ListHeader(page, "待確認訂單清單")!;
        Assert.Equal(["900000000000001", "900000000000002"], CoupangScreens.ListedOrders(page, header));
        Assert.Equal(271, CoupangScreens.SelectAll(page, header).Left);
        Assert.Equal(1772, CoupangScreens.ConfirmOrdersButton(page)!.Left);
    }

    [Fact]
    public void Reads_the_accept_and_done_dialogs()
    {
        var accept = new List<OcrBox>
        {
            B("確認接受訂單", 742, 290, 120), B("您想接受以下1個訂單並開始準備發貨?", 742, 357, 240), B("訂單編號:", 742, 398, 60),
            B("900000000000001", 762, 419, 124), B("取消", 1005, 600, 32), B("確認", 1111, 600, 32),
            B("900000000000009", 326, 726, 106), B("確認訂單", 1785, 873, 72, 24)
        };
        var (named, orders, confirm) = CoupangScreens.AcceptDialog(accept)!.Value;
        Assert.Equal(1, named);
        Assert.Equal(["900000000000001"], orders);
        Assert.Equal(1111, confirm.Left);
        Assert.Null(CoupangScreens.AcceptDialog([B("確認訂單", 1785, 873)]));

        var done = new List<OcrBox> { B("配送處理完成。", 772, 373, 116), B("您選擇的物品已完成配送處理。", 738, 430, 180), B("確認", 1107, 518, 32), B("確認訂單", 1785, 873, 72, 24) };
        Assert.Equal(1107, CoupangScreens.DoneDialogConfirm(done)!.Left);
    }
}
