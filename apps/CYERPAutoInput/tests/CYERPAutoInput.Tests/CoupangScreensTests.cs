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
        B("點擊", 1409, 1001, 28), B("確認訂單", 1440, 1001, 56), B("後，其他狀態會變更為", 1498, 1001, 130), B("確認訂單", 1772, 998, 72, 24)
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

    [Fact]
    public void Reads_the_ship_channels_bar_and_invoice_reminder()
    {
        var page = new List<OcrBox>
        {
            B("待安排配送清單", 245, 400, 126), B("共 1 筆", 386, 403, 42), B("已選 0 筆", 461, 403, 55),
            B("訂單編號", 350, 626, 52), B("配送通路", 649, 626, 52), B("寄件單號", 784, 626, 52),
            B("900000000000001", 323, 749, 106), B("宅配 - 新竹物流", 629, 749, 92), B("安排出貨", 783, 740, 52),
            B("選擇商品後，點擊安排配送即可生成配送貨運單號。", 1158, 873, 314), B("宅配 - 新竹物流", 1513, 873, 190, 26), B("批次安排配送", 1729, 870, 150, 30)
        };
        var header = CoupangScreens.ListHeader(page, "待安排配送清單")!;
        Assert.Equal(["宅配-新竹物流"], CoupangScreens.ColumnValues(page, header, "配送通路"));
        var (select, arrange) = CoupangScreens.ShipBar(page)!.Value;
        Assert.Equal((1513, 1729), (select.Left, arrange.Left));

        var reminder = new List<OcrBox>
        {
            B("請確認發票開立需求", 358, 343, 162), B("宅配：下午3點前下單者適用當日取件服務。", 346, 463, 280),
            B(ShopeeBatchScreens.NamelessCheckBox, 330, 550, 16, 16), B("不用再提醒我", 354, 550, 84), B("確認", 1536, 547, 30)
        };
        var (dont, confirm) = CoupangScreens.InvoiceReminder(reminder)!.Value;
        Assert.Equal((330, 1536), (dont.Left, confirm.Left));
        Assert.Null(CoupangScreens.InvoiceReminder(page));
    }

    [Fact]
    public void Reads_the_arrange_dialog_and_pickup_details()
    {
        // Laid out like the user's 安排出貨 screenshot (2026-10-19); address made up.
        var arrange = new List<OcrBox>
        {
            B("安排出貨", 584, 222, 76), B("安排出貨後將無法更改，請確認物流取件日期後再進行提交", 630, 304, 364),
            B("配送方式", 584, 367, 56), B("宅配 - 新竹物流", 784, 367, 98), B("取貨日期", 584, 416, 56), B("2026-10-19 (星期一) * 當日取件", 784, 416, 208),
            B("取貨地址", 584, 487, 56), B("(800) 高雄市 示範區 測試路1號", 796, 476, 230), B("王小明 0900-000-000", 796, 497, 140), B("編輯", 1282, 486, 28),
            B("備註", 584, 557, 28), B("取消", 1167, 641, 32), B("提交", 1273, 641, 32),
            B("安排出貨", 787, 727, 56), B("900000000000001", 328, 737, 106)
        };
        var (method, matches, submit) = CoupangScreens.ArrangeDialog(arrange, "高雄市示範區 測試路1號")!.Value;
        Assert.Equal(("宅配-新竹物流", (bool?)true, 1273), (method, matches, submit.Left));
        Assert.False(CoupangScreens.ArrangeDialog(arrange, "高雄市示範區測試路2號")!.Value.AddressMatches);

        // The cell and its link both read the 訂單編號 (user LOG 2026-10-19: counted as 2 rows).
        var details = new List<OcrBox>
        {
            B("取貨詳情", 72, 64, 80), B("訂單編號", 74, 132, 205, 52), B("寄件單號", 755, 132, 130, 52), B("狀態", 886, 132, 80, 52),
            B("900000000000001", 74, 184, 205, 113), B("900000000000001", 122, 233, 106, 18), B("1800000000", 755, 184, 130, 113), B("成功", 886, 184, 80, 113),
            B("配送方式", 85, 392, 56), B("宅配 - 新竹物流", 85, 410, 98),
            B("關閉", 748, 528, 92, 38), B("列印寄件單", 855, 528, 112, 38)
        };
        var (orders, succeeded, shipMethod, print, close) = CoupangScreens.PickupDetails(details)!.Value;
        Assert.Equal(["900000000000001"], orders);
        Assert.Equal(["900000000000001"], succeeded);
        Assert.Equal(("宅配-新竹物流", 855, 748), (shipMethod, print.Left, close.Left));
        // Still loading: no 成功 yet.
        var loading = details.Where(b => b.Text != "成功").ToList();
        Assert.Empty(CoupangScreens.PickupDetails(loading)!.Value.Succeeded);
    }

    [Fact]
    public void Reads_channels_without_their_header_and_with_other_dashes()
    {
        var page = new List<OcrBox>
        {
            B("待安排配送清單", 245, 400, 126), B("訂單編號", 350, 626, 52),
            B("900000000000001", 323, 749, 106), B("宅配 – 新竹物流", 629, 749, 92), B("900000000000002", 323, 900, 106), B("便利商店 - 7-11", 629, 900, 92),
            B("宅配 - 嘉里", 1513, 1010, 190, 26), B("批次安排配送", 1729, 1007, 150, 30)
        };
        var header = CoupangScreens.ListHeader(page, "待安排配送清單")!;
        Assert.Equal(["宅配-新竹物流", "便利商店-7-11"], CoupangScreens.ColumnValues(page, header, "配送通路"));
        Assert.Equal("宅配-嘉里", CoupangScreens.Channel(ShopeeScreenshot.Compact(CoupangScreens.ShipBar(page)!.Value.Select.Text)));
    }

    [Fact]
    public void Reads_rows_that_start_where_the_tall_header_cells_end()
    {
        // As the user's LOG read 配送準備 (2026-10-19): header cells 611–668, the row's cells from 668.
        var page = new List<OcrBox>
        {
            B("待安排配送清單", 245, 400, 126), B("共 1 筆", 386, 403, 42), B("已選 0 筆", 461, 403, 55),
            B("訂單編號", 311, 611, 140, 57), B("分箱出貨", 451, 611, 93, 57), B("配送通路", 617, 611, 124, 57), B("寄件單號", 741, 611, 148, 57),
            B(ShopeeBatchScreens.NamelessCheckBox, 271, 630, 18, 18),
            B("900000000000001", 311, 668, 140, 64), B("宅配-新竹物流", 617, 668, 124, 64), B("安排出貨發票種類:紙本發票", 741, 668, 148, 64),
            B(ShopeeBatchScreens.NamelessCheckBox, 271, 732, 18, 18), B("900000000000001", 328, 732, 106, 18),
            B("批次安排配送", 1729, 863, 150, 30), B("請選擇配送方式", 1513, 867, 190, 26), B("安排配送", 1275, 876, 56)
        };
        var header = CoupangScreens.ListHeader(page, "待安排配送清單")!;
        Assert.Equal(["900000000000001"], CoupangScreens.ListedOrders(page, header));
        Assert.Equal(["宅配-新竹物流"], CoupangScreens.ColumnValues(page, header, "配送通路"));
        Assert.Equal(271, CoupangScreens.SelectAll(page, header).Left);
        Assert.Equal("請選擇配送方式", CoupangScreens.ShipBar(page)!.Value.Select.Text);
    }

    [Fact]
    public void Reads_the_convenience_store_dialog()
    {
        // 便利商店 (user's screenshot 2026-10-20): 「申請寄件編號」 with a 退貨超商, no 取貨地址.
        var page = new List<OcrBox>
        {
            B("申請寄件編號", 584, 362, 120), B("Family Mart", 826, 520, 90), B("退貨超商", 584, 578, 56), B("全家示範店", 796, 568, 100),
            B("編輯", 1040, 592, 28), B("取消", 1167, 776, 32), B("提交", 1273, 776, 32), B("安排出貨", 787, 836, 56)
        };
        var (method, matches, submit) = CoupangScreens.ArrangeDialog(page, "高雄市示範區測試路1號")!.Value;
        Assert.Equal(("", (bool?)null, 1273), (method, matches, submit.Left));
    }
}
