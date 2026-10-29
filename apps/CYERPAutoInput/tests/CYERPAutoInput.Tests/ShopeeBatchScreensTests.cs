using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>蝦皮全自動 screens, laid out like the seller centre; every number here is made up.</summary>
public sealed class ShopeeBatchScreensTests
{
    private static OcrBox B(string text, int left, int top, int width = 0, int height = 20) =>
        new(text, left, top, width > 0 ? width : text.Length * 14, height);

    private static List<OcrBox> ListHeader(string todayChip) =>
    [
        B("首頁", 66, 142), B("我的銷售", 137, 142),
        B("待出貨 (63)", 412, 287), B("運送中 (317)", 525, 287),
        B("訂單狀態", 245, 366), B("全部", 343, 366), B("待處理 (31)", 419, 366), B("已處理 (32)", 544, 366),
        B("建議出貨期限", 245, 419), B("全部", 374, 419), B("已延遲出貨 (0)", 451, 419), B(todayChip, 597, 419), B("建議明天內出貨 (31)", 773, 419),
        B("物流方式", 951, 472), B("所有運送方式", 1034, 472),
        B("32 包裝", 245, 533),
        B("商品", 264, 594), B("買家實付金額", 776, 594), B("狀態", 967, 594), B("到期時間", 1019, 594), B("物流方式", 1287, 594), B("操作", 1543, 594)
    ];

    [Fact]
    public void Reads_an_empty_today_list_and_finds_the_deadline_all()
    {
        var boxes = ListHeader("建議今天內出貨 (0)").Concat([B("找不到訂單", 960, 770), B("請重新載入", 990, 795)]).ToList();
        Assert.True(ShopeeBatchScreens.IsOrderList(boxes));
        Assert.True(ShopeeBatchScreens.NoOrders(boxes));
        Assert.Equal(0, ShopeeBatchScreens.TodayCount(boxes));
        Assert.Equal(31, ShopeeBatchScreens.PendingCount(boxes));
        Assert.Null(ShopeeBatchScreens.FirstOrder(boxes));
        // The 全部 of 建議出貨期限, not the one of 訂單狀態.
        Assert.Equal(419, ShopeeBatchScreens.DeadlineAll(boxes)!.Top);
    }

    [Fact]
    public void Takes_the_first_order_below_the_header_with_its_carrier()
    {
        var boxes = ListHeader("建議今天內出貨 (2)").Concat(
        [
            B("buyer_one", 300, 658), B("訂單編號 261012AB1C23D4", 1587, 658),
            B("滿額折價【測試】商品一", 334, 708), B("x1", 732, 708), B("NT$318", 775, 708),
            B("待出貨", 967, 708), B("為了避免延遲出貨，請在 2026/10/13 19:00 之前 出", 967, 732),
            B("蝦皮店到店 - 隔日到貨", 1287, 708), B("安排出貨", 1544, 708),
            B("buyer_two", 300, 990), B("訂單編號 261012XY9Z87W6", 1587, 990), B("待出貨", 967, 1040), B("7-ELEVEN", 1287, 1040)
        ]).ToList();
        var first = ShopeeBatchScreens.FirstOrder(boxes)!;
        Assert.Equal("261012AB1C23D4", first.OrderSn);
        Assert.Equal(708, first.Status.Top);
        // The second order too, for when the first is passed over.
        Assert.Equal(["261012AB1C23D4", "261012XY9Z87W6"], ShopeeBatchScreens.Orders(boxes).Select(o => o.OrderSn));
        Assert.True(ShopeeBatchScreens.SupportedCarrier("蝦皮店到宅"));
        Assert.True(ShopeeBatchScreens.SupportedCarrier("7-ELEVEN"));
        Assert.True(ShopeeBatchScreens.SupportedCarrier("萊爾富"));
        Assert.False(ShopeeBatchScreens.SupportedCarrier("OK超商"));
    }

    private static List<OcrBox> Detail(params OcrBox[] card) =>
    [
        B("首頁", 66, 30), B("我的銷售", 136, 30), B("訂單明細", 241, 30),
        B("待出貨", 309, 125), B("為了避免延遲出貨，請在 2026/10/13 19:00 之前 出貨", 309, 165),
        .. card,
        B("訂單編號", 309, 586), B("261012AB1C23D4", 309, 621),
        B("運送資訊", 309, 668), B("包裹1:", 309, 709), B("蝦皮店到店 - 隔日到貨", 360, 709)
    ];

    [Fact]
    public void Finds_the_ship_button_and_never_the_cancel_button()
    {
        var boxes = Detail(B("不成立訂單", 1003, 238), B("安排出貨", 1125, 238));
        Assert.True(ShopeeBatchScreens.IsOrderDetail(boxes));
        Assert.Equal("261012AB1C23D4", ShopeeBatchScreens.DetailOrderSn(boxes));
        Assert.Equal("蝦皮店到店-隔日到貨", ShopeeBatchScreens.DetailCarrier(boxes));
        Assert.Equal(1125, ShopeeBatchScreens.ShipButton(boxes)!.Left);
        Assert.Equal("安排取件", ShopeeBatchScreens.ShipButton(Detail(B("不成立訂單", 1003, 238), B("安排取件", 1125, 238)))!.Text);

        // Only 不成立訂單, the two read as one, or the button overlapping it: nothing to click.
        Assert.Null(ShopeeBatchScreens.ShipButton(Detail(B("不成立訂單", 1003, 238))));
        Assert.Null(ShopeeBatchScreens.ShipButton(Detail(B("不成立訂單安排出貨", 1003, 238))));
        Assert.Null(ShopeeBatchScreens.ShipButton(Detail(B("不成立訂單", 1003, 238), B("安排出貨", 1060, 238))));
        // Shipped already: no button.
        Assert.Null(ShopeeBatchScreens.ShipButton(Detail()));
    }

    [Fact]
    public void Waits_for_the_tracking_number_in_the_dialog()
    {
        List<OcrBox> requesting = [B("查看寄件編號", 60, 66), B("寄件編號申請中……", 297, 120), B("確認", 610, 778)];
        Assert.NotNull(ShopeeBatchScreens.TrackingDialogTitle(requesting));
        Assert.True(ShopeeBatchScreens.TrackingRequested(requesting));
        Assert.Equal("", ShopeeBatchScreens.DialogTracking(requesting));

        List<OcrBox> ready = [B("查看寄件編號", 52, 60), B("TW1 234 567 890 12O", 278, 112), B("請盡快將包裹交寄。", 278, 135), B("列印寄件單", 552, 808)];
        Assert.False(ShopeeBatchScreens.TrackingRequested(ready));
        Assert.Equal("TW123456789012O", ShopeeBatchScreens.DialogTracking(ready));
        Assert.Equal(552, ShopeeBatchScreens.PrintLabelButton(ready)!.Left);
    }

    [Fact]
    public void Reads_the_label_preview()
    {
        List<OcrBox> loading = [B("預覽列印", 18, 149), B("列印出貨文件", 1790, 165), B("0%", 1204, 584), B("下載出貨文件中", 884, 632)];
        Assert.True(ShopeeBatchScreens.IsAwbPage(loading));
        Assert.True(ShopeeBatchScreens.AwbDownloading(loading));
        Assert.Null(ShopeeBatchScreens.AwbDownloaded(loading));

        List<OcrBox> done =
        [
            B("預覽列印", 17, 149), B("出貨文件已下載完成。已成功下載1個包裹中的1個包裹的出貨文件。0個包裹下載失敗。", 33, 182),
            B("列印出貨文件", 1790, 177),
            B("蝦皮訂單編號: 261012AB1C23D4", 750, 422, 180, 14), B("寄件編號: TW1234567890120", 750, 438, 180, 14)
        ];
        Assert.Equal(new ShopeeAwbDownload(1, 1, 0), ShopeeBatchScreens.AwbDownloaded(done));
        Assert.Equal("261012AB1C23D4", ShopeeBatchScreens.LabelOrderSn(done));
        Assert.Equal("TW1234567890120", ShopeeBatchScreens.LabelTracking(done));
        Assert.Equal(1790, ShopeeBatchScreens.PrintDocumentsButton(done)!.Left);
        // The last character of a TW number may be a letter or a digit; the rest are digits.
        Assert.True(ShopeeBatchScreens.SameTracking("TW1 234 567 890 120", "TW1234567890120"));
        Assert.True(ShopeeBatchScreens.SameTracking("TWI23456789O120", "TW1234567890120"));
        // …O on one screen, …0 on the next: one number.
        Assert.True(ShopeeBatchScreens.SameTracking("TW123456789012O", "TW1234567890120"));
        Assert.False(ShopeeBatchScreens.SameTracking("TW123456789013O", "TW1234567890120"));
        Assert.False(ShopeeBatchScreens.SameTracking("", ""));
    }

    [Fact]
    public void Reads_whole_lines_as_the_ocr_returns_them()
    {
        // Line boxes as read on the user's screens (made-up numbers): 内 for 內, 包里 for 包裹,
        // and a 「在1天內確認」 tag that puts 待出貨 below the carrier.
        var list = ListHeader("建議今天内出貨(28)").Concat(
        [
            B("buyer_one", 297, 657), B("訂單編號261011AB12CD34", 1581, 657),
            B("2件9.9折", 261, 715), B("NT$208", 770, 704), B("在1天内確認", 965, 704), B("蝦皮店到店", 1283, 707), B("安排出貨", 1541, 708),
            B("貨到付款", 772, 729), B("待出貨", 963, 732), B("為了避免延逗出貨，請在2026/10/13之前出貨", 964, 755),
            B("buyer_two", 297, 872), B("訂單編號261011XY98ZW76", 1577, 873), B("在1天內確認", 965, 920), B("7-ELEVEN", 1284, 922), B("待出貨", 962, 945)
        ]).ToList();
        Assert.Equal(28, ShopeeBatchScreens.TodayCount(list));
        list = list.Select(b => b.Text.StartsWith("待處理") ? b with { Text = "待虚理 (30)" } : b).ToList();
        Assert.True(ShopeeBatchScreens.IsOrderList(list));
        Assert.Equal(30, ShopeeBatchScreens.PendingCount(list));
        Assert.Equal(597, ShopeeBatchScreens.TodayChip(list)!.Left);
        var first = ShopeeBatchScreens.FirstOrder(list)!;
        Assert.Equal("261011AB12CD34", first.OrderSn);
        Assert.Equal(732, first.Status.Top);
        Assert.Equal(2, ShopeeBatchScreens.Orders(list).Count);

        List<OcrBox> awb = [B("預覽列印", 13, 137), B("出貨文件已下載完成。已成功下載1個包里中的1個包里的出貨文件・0個包里下載失敗。", 28, 171), B("列印出货文件", 1783, 165)];
        Assert.Equal(new ShopeeAwbDownload(1, 1, 0), ShopeeBatchScreens.AwbDownloaded(awb));
        Assert.NotNull(ShopeeBatchScreens.PrintDocumentsButton(awb));

        List<OcrBox> dialog = [B("查看寄件編號", 53, 47, 135, 25), B("X", 640, 52, 16, 16), B("列印寄件單", 546, 797)];
        Assert.Equal(640, ShopeeBatchScreens.DialogCloseMark(dialog, dialog[0])!.Left);
        Assert.Equal("蝦皮店到店-隔日到貨#TW1234567", ShopeeBatchScreens.DetailCarrier([B("包裹1：蝦皮店到店-隔日到貨", 305, 828), B("#TW1234567", 516, 827)]));
    }

    [Fact]
    public void Handles_seven_eleven()
    {
        Assert.Equal(ShopeeBatchCarrier.SevenEleven, ShopeeBatchScreens.CarrierKind("7-ELEVEN"));
        Assert.Equal(ShopeeBatchCarrier.Shopee, ShopeeBatchScreens.CarrierKind("蝦皮店到店-隔日到貨"));
        Assert.Equal(ShopeeBatchCarrier.HiLife, ShopeeBatchScreens.CarrierKind("萊爾富"));
        Assert.Equal(ShopeeBatchCarrier.Other, ShopeeBatchScreens.CarrierKind("OK超商"));

        List<OcrBox> dialog = [B("查看寄件編號", 35, 35), B("G12 345 678 9O1", 244, 89), B("寄件編號將於2026/10/1523:59失效", 245, 119), B("列印寄件單", 545, 711)];
        Assert.Equal("G12345678901", ShopeeBatchScreens.DialogTracking(dialog));

        List<OcrBox> slip =
        [
            B("統一超商", 125, 142), B("交貨便服務代碼：G12345678901", 75, 317), B("物流條碼：833001G123456789", 19, 431),
            B("寄貨訂單編號：261011AB12CD34", 19, 469)
        ];
        Assert.True(ShopeeBatchScreens.IsSevenLabelPage(slip));
        Assert.False(ShopeeBatchScreens.IsPrintDialog(slip));
        Assert.Equal("261011AB12CD34", ShopeeBatchScreens.LabelOrderSn(slip));
        Assert.Equal("G12345678901", ShopeeBatchScreens.SevenLabelCode(slip));
        Assert.True(ShopeeBatchScreens.SameTracking("G12 345 678 9O1", "G12345678901"));
        Assert.False(ShopeeBatchScreens.SameTracking("G12345678902", "G12345678901"));

        // 縮放比例 only under 顯示更多設定.
        List<OcrBox> folded = [B("列印", 48, 30), B("目的地", 51, 90), B("GG-D410 Printer", 227, 91), B("份數", 51, 196), B("顯示更多設定", 50, 324)];
        Assert.True(ShopeeBatchScreens.IsPrintDialog(folded));
        Assert.Null(ShopeeBatchScreens.PrintScale(folded));
        Assert.Equal(324, ShopeeBatchScreens.MoreSettings(folded)!.Top);

        // 目的地 另存為 PDF (酷澎 2026-10-20): no 縮放比例 or 更多設定, the button is 儲存.
        List<OcrBox> pdf =
        [
            B("列印", 25, 52), B("1頁", 338, 54), B("目的地", 25, 110), B("另存為PDF", 200, 110), B("頁面", 25, 164), B("全部", 180, 164),
            B("每張工作表頁數", 25, 218), B("邊界", 25, 272), B("最小值", 180, 272), B("選項", 25, 326), B("頁首及頁尾", 200, 326),
            B("背景圖形", 200, 364), B("儲存", 238, 840), B("取消", 314, 840)
        ];
        Assert.True(ShopeeBatchScreens.IsPrintDialog(pdf));
        Assert.True(ShopeeBatchScreens.SamePrinter(ShopeeBatchScreens.PrintDestination(pdf), "另存為 PDF"));
        Assert.Equal(238, ShopeeBatchScreens.SaveButton(pdf)!.Left);
        Assert.Null(ShopeeBatchScreens.PrintButton(pdf));
    }

    [Fact]
    public void Handles_familymart()
    {
        Assert.Equal(ShopeeBatchCarrier.FamilyMart, ShopeeBatchScreens.CarrierKind("全家"));
        List<OcrBox> ask = [B("查看寄件編號", 94, 57), B("使用說明", 111, 192), B("申請寄件編號", 600, 553)];
        Assert.NotNull(ShopeeBatchScreens.ApplyTrackingButton(ask));
        Assert.Null(ShopeeBatchScreens.ApplyTrackingButton([B("寄件編號申請中……", 330, 110)]));
        // A number format not seen yet: the line right under the title.
        List<OcrBox> code = [B("查看寄件編號", 94, 57, 138, 29), B("12 3456 7890", 330, 105), B("寄件編號將於2026/10/1523:59失效", 330, 135)];
        Assert.Equal("1234567890", ShopeeBatchScreens.DialogTracking(code));

        List<OcrBox> slip = [B("FamilyMart寄取貨單", 694, 138), B("訂单编號：16351972389", 676, 213), B("蝦皮订單编號：", 677, 245), B("261011AB12CD34", 679, 261)];
        Assert.True(ShopeeBatchScreens.IsFamilyLabelPage(slip));
        Assert.True(ShopeeBatchScreens.ShowsOrderSn(slip, "261011AB12CD34"));
        Assert.False(ShopeeBatchScreens.ShowsOrderSn(slip, "261011AB12CD35"));
        Assert.True(ShopeeBatchScreens.ShowsNumber(slip, "16351972389"));
        Assert.False(ShopeeBatchScreens.ShowsNumber(slip, "16351972380"));
        // 全家's 寄件編號: 11 digits under the title.
        Assert.Equal("16351972389", ShopeeBatchScreens.DialogTracking([B("查看寄件編號", 94, 57, 138, 29), B("16351972389", 330, 105)]));
        Assert.True(ShopeeBatchScreens.SameTracking("# 16351972389", "16351972389"));

        List<OcrBox> dialog =
        [
            B("列印", 961, 33), B("3張纸", 1257, 32), B("目的地", 962, 92), B("GG-D410 Printer", 1138, 93),
            B("頁面", 963, 148), B("自訂", 1117, 147), B("2", 1117, 186), B("份数", 962, 239),
            B("縮放比例", 961, 507), B("自訂", 1118, 509), B("165", 1116, 547), B("列印", 1176, 821), B("取消", 1251, 822)
        ];
        Assert.Equal(3, ShopeeBatchScreens.SheetCount(dialog));
        Assert.Equal("自訂", ShopeeBatchScreens.PrintRowValue(dialog, "頁面")!.Text);
        Assert.Equal("2", ShopeeBatchScreens.PrintPagesBox(dialog)!.Text);
        Assert.Equal("自訂", ShopeeBatchScreens.PrintRowValue(dialog, "縮放比例")!.Text);
        Assert.Equal(165, ShopeeBatchScreens.PrintScale(dialog));
        Assert.Null(ShopeeBatchScreens.PrintPagesBox(dialog.Where(b => b.Text != "2").ToList()));
    }

    [Fact]
    public void Reads_the_page_text_shapes()
    {
        // As Chrome gives page text (made-up values): labels and counts apart, whole buttons
        // 7 px apart, each list row one link with the row's text.
        List<OcrBox> list =
        [
            B("我的銷售", 244, 109), B("待處理", 418, 246), B("(19)", 464, 246, 33),
            B("建議出貨期限", 244, 299), B("全部", 372, 300), B("建議今天內出貨", 595, 300), B("(18)", 703, 300, 33),
            B("買家實付金額", 774, 474), B("狀態", 966, 474), B("物流方式", 1286, 474),
            new("buyer 訂單編號 261011AB12CD34 2 件 9.9折 【測試】商品 NT$208 待出貨 蝦皮店到店 安排出貨", 244, 524, 1556, 277),
            B("安排出貨", 1542, 586, 65, 21)
        ];
        Assert.Equal(19, ShopeeBatchScreens.PendingCount(list));
        Assert.Equal(18, ShopeeBatchScreens.TodayCount(list));
        var first = ShopeeBatchScreens.FirstOrder(list)!;
        Assert.Equal("261011AB12CD34", first.OrderSn);
        Assert.InRange(first.Status.CenterX, 940, 1000);
        Assert.True(first.Status.Right < 1542); // never the row's 安排出貨

        var detail = new List<OcrBox> { B("訂單明細", 241, 22), B("待出貨", 310, 125), new("不成立訂單", 985, 228, 116, 36), new("安排出貨", 1108, 228, 100, 36), B("訂單編號", 310, 585) };
        Assert.Equal(1108, ShopeeBatchScreens.ShipButton(detail)!.Left);
        Assert.Null(ShopeeBatchScreens.ShipButton([.. detail.Take(2), new("不成立訂單", 985, 228, 116, 36), new("安排出貨", 1099, 228, 100, 36)]));

        Assert.NotNull(ShopeeBatchScreens.PrintDocumentsButton([new(" 列印出貨文件", 1746, 39, 157, 36)]));
        Assert.True(ShopeeBatchScreens.IsAwbPage([new(" 列印出貨文件", 1746, 39, 157, 36)]));
        List<OcrBox> dialog = [new("查看寄件編號", 648, 72, 133, 26), new(ShopeeBatchScreens.NamelessButton, 1226, 70, 30, 30)];
        Assert.Equal(1226, ShopeeBatchScreens.DialogCloseMark(dialog, dialog[0])!.Left);
    }

    [Fact]
    public void Checks_chromes_print_dialog()
    {
        List<OcrBox> dialog =
        [
            B("列印", 972, 46), B("1 張紙", 1270, 46),
            B("目的地", 972, 102), B("GG-D410 Printer", 1144, 102),
            B("頁面", 972, 156), B("全部", 1127, 156),
            B("縮放比例", 972, 374), B("自訂", 1127, 374), B("110", 1127, 412),
            B("列印", 1187, 831), B("取消", 1262, 831)
        ];
        Assert.True(ShopeeBatchScreens.IsPrintDialog(dialog));
        Assert.Equal("GG-D410 Printer", ShopeeBatchScreens.PrintDestination(dialog));
        Assert.True(ShopeeBatchScreens.SamePrinter(ShopeeBatchScreens.PrintDestination(dialog), "GG-D410 Printer"));
        Assert.True(ShopeeBatchScreens.SamePrinter("GG-D410Printer", "gg-d410 printer"));
        Assert.False(ShopeeBatchScreens.SamePrinter("Microsoft Print to PDF", "GG-D410 Printer"));
        Assert.Equal(110, ShopeeBatchScreens.PrintScale(dialog));
        // The button, not the dialog's title.
        Assert.Equal(831, ShopeeBatchScreens.PrintButton(dialog)!.Top);
    }

    [Fact]
    public void Finds_the_set_printer_in_the_destination_list()
    {
        List<OcrBox> list =
        [
            B("目的地", 972, 102), B("Brother HL-L2320D series", 1144, 102),
            B("Brother HL-L2320D series", 1144, 140), B("GG-D410 Printer", 1144, 176),
            B("Xprinter XP-420B", 1144, 212), B("另存為 PDF", 1144, 248), B("顯示更多...", 1144, 284)
        ];
        Assert.False(ShopeeBatchScreens.SamePrinter(ShopeeBatchScreens.PrintDestination(list.Take(2).ToList()), "GG-D410"));
        Assert.Equal(176, ShopeeBatchScreens.PrinterOption(list, "GG-D410")!.Top);
        // The closed list's own value is not an option.
        Assert.Null(ShopeeBatchScreens.PrinterOption([B("目的地", 972, 102), B("GG-D410 Printer", 1144, 102)], "GG-D410 Printer"));
        Assert.Null(ShopeeBatchScreens.PrinterOption(list, "Microsoft Print to PDF"));
    }

    [Fact]
    public void Finds_the_dialog_cross_right_of_the_title()
    {
        // A white dialog (x 100–600) on a dimmed page, the title at y 50 and a grey × around (560, 60).
        int Pixel(int x, int y)
        {
            if (x < 100 || x >= 600 || y < 20 || y >= 190) return 120;
            var dx = x - 560;
            var dy = y - 60;
            return Math.Abs(dx) <= 7 && Math.Abs(dy) <= 7 && (Math.Abs(dx - dy) <= 1 || Math.Abs(dx + dy) <= 1) ? 140 : 255;
        }
        var cross = ShopeeBatchScreens.CloseCross(Pixel, 700, 200, B("查看寄件編號", 120, 50, 150, 20))!.Value;
        Assert.InRange(cross.X, 557, 563);
        Assert.InRange(cross.Y, 57, 63);
    }

    [Fact]
    public void Label_numbers_in_their_own_text_on_the_row_or_below()
    {
        // 隔日到貨 labels (user's LOG, 2026-10-14): the label and the number need not be one piece.
        var sameRow = new List<OcrBox> { B("蝦皮訂單編號:", 10, 300, 90), B("261014AB12CD34", 104, 300, 120), B("寄件編號:", 10, 322, 70), B("TW260000000001H", 84, 322, 130) };
        Assert.Equal("261014AB12CD34", ShopeeBatchScreens.LabelOrderSn(sameRow));
        Assert.Equal("TW260000000001H", ShopeeBatchScreens.LabelTracking(sameRow));

        var below = new List<OcrBox> { B("蝦皮訂單編號", 10, 300, 90), B("261014AB12CD34", 10, 322, 120) };
        Assert.Equal("261014AB12CD34", ShopeeBatchScreens.LabelOrderSn(below));

        var oneBlock = new List<OcrBox> { B("2026-10-14 11:25:11 門市交寄 蝦皮訂單編號: 261014AB12CD34 寄件編號: TW260000000001H", 10, 300, 300, 80) };
        Assert.Equal("261014AB12CD34", ShopeeBatchScreens.LabelOrderSn(oneBlock));
        Assert.Equal("TW260000000001H", ShopeeBatchScreens.LabelTracking(oneBlock));
    }

    [Fact]
    public void Next_day_label_page_without_the_order_number_in_its_text()
    {
        // Laid out like the user's LOG (2026-10-14): the PDF's text has no 蝦皮訂單編號 line.
        var page = new List<OcrBox>
        {
            B("預覽列印", 17, 17, 71, 22),
            B("出貨文件已下載完成。已成功下載1個包裹中的1個包裹的出貨文件。0個包裹下載失敗。", 32, 49, 1700, 24),
            B(" 列印出貨文件", 1746, 39, 157, 36),
            B("第 1 頁", 741, 116, 436, 615),
            B("寄件編號: TW260000000001Z", 753, 310, 174, 14),
            B("門市交寄  隔日到貨", 754, 128, 206, 145)
        };
        Assert.True(ShopeeBatchScreens.IsAwbPage(page));
        Assert.True(ShopeeBatchScreens.LabelShown(page));
        Assert.Equal(new OcrBox("第 1 頁", 741, 116, 436, 615), ShopeeBatchScreens.LabelPageBox(page));
        Assert.Equal("", ShopeeBatchScreens.LabelOrderSn(page)); // so the label is read with OCR
        Assert.False(ShopeeBatchScreens.LabelShown([B("預覽列印", 17, 17, 71, 22)]));
        Assert.True(ShopeeBatchScreens.SameOrderSn("261014AB12CDO4", "261014AB12CD04"));
        Assert.False(ShopeeBatchScreens.SameOrderSn("261014AB12CD35", "261014AB12CD34"));
    }

    [Fact]
    public void Hct_pickup_dialog_checks_the_set_address_and_finds_confirm()
    {
        Assert.Equal(ShopeeBatchCarrier.Hct, ShopeeBatchScreens.CarrierKind("新竹物流"));
        // Laid out like the user's 安排取件 dialog (2026-10-14); name, phone and address are made up.
        var dialog = new List<OcrBox>
        {
            B("安排取件", 46, 50, 90, 26), B("買家取件物流", 46, 105, 100), B("新竹物流", 46, 135, 70),
            B("最快取件日期", 46, 186, 100), B("14-10-2026", 60, 228, 80), B("註記", 46, 315, 40), B("Input", 60, 348, 40),
            B("快遞向你取件的地址", 46, 410, 150), B("王小明 0900000000", 64, 460, 160), B("取件", 214, 460, 30), B("變更", 249, 460, 30),
            B("100 臺北市中正區測試路1號", 64, 485, 200),
            B("成功安排取件後，請列印寄件單，並將寄件單黏貼至包裹外箱上，否則快遞將無法成功取件。", 60, 560, 580),
            B("取消", 484, 636, 78, 34), B("確認", 574, 636, 78, 34)
        };
        Assert.True(ShopeeBatchScreens.PickupAddressMatches(dialog, "100 臺北市中正區測試路1號"));
        Assert.True(ShopeeBatchScreens.PickupAddressMatches(dialog, "100臺北市中正區測試路1號"));
        Assert.False(ShopeeBatchScreens.PickupAddressMatches(dialog, "100 臺北市中正區測試路2號"));
        Assert.False(ShopeeBatchScreens.PickupAddressMatches(dialog, ""));
        Assert.Equal("確認", ShopeeBatchScreens.PickupConfirmButton(dialog)!.Text);
        Assert.Equal("取消", ShopeeBatchScreens.PickupCancelButton(dialog)!.Text);
        // The order page alone (its own 安排取件 button) is not the dialog.
        Assert.Null(ShopeeBatchScreens.PickupConfirmButton([B("待出貨", 310, 120, 60), B("不成立訂單", 986, 226, 112, 34), B("安排取件", 1109, 226, 98, 34)]));

        var details = new List<OcrBox> { B("快遞取件詳情", 54, 50, 130, 26), B("506 371 934 5", 324, 108, 130, 26), B("列印寄件單", 546, 782, 112, 34) };
        Assert.NotNull(ShopeeBatchScreens.TrackingDialogTitle(details));
        Assert.Equal("5063719345", ShopeeBatchScreens.DialogTracking(details));
    }

    [Fact]
    public void Hct_label_prints_the_numbers_bare()
    {
        var label = new List<OcrBox> { B("宅配", 776, 305, 30), B("5063719345", 890, 302, 90), B("261013AB12CD34", 795, 452, 130), B("王小明", 795, 470, 50) };
        Assert.Equal("", ShopeeBatchScreens.LabelOrderSn(label));
        Assert.True(ShopeeBatchScreens.ShowsOrderSn(label, "261013AB12CD34"));
        Assert.True(ShopeeBatchScreens.ShowsNumber(label, "5063719345"));
        Assert.True(ShopeeBatchScreens.ShowsOrderSnLoosely([B("261013AB12CDO4", 0, 0, 130)], "261013AB12CD04"));
    }

    [Fact]
    public void Family_apply_button_is_not_the_dialog_title()
    {
        // Laid out like the user's 申請寄件編號 dialog (2026-10-14).
        var dialog = new List<OcrBox>
        {
            B("申請寄件編號", 650, 308, 130, 24), B("寄件人真實姓名", 650, 370, 110), B("王小明", 664, 404, 60),
            B("取消", 1035, 580, 78, 34), B("申請寄件編號", 1127, 580, 128, 34)
        };
        Assert.Equal(1127, ShopeeBatchScreens.ApplyTrackingButton(dialog)!.Left);
        Assert.Null(ShopeeBatchScreens.ApplyTrackingButton([B("申請寄件編號", 650, 308, 130, 24)]));
        Assert.Equal(ShopeeBatchCarrier.Shopee, ShopeeBatchScreens.CarrierKind("店到家宅配"));
    }

    [Fact]
    public void Buyer_note_and_chat_controls()
    {
        // Laid out like the user's order page and 聊聊 (2026-10-14); names and numbers made up.
        var page = new List<OcrBox>
        {
            B("買家測試", 136, 506, 80), B("關注", 830, 499, 98, 36), B(" 聊聊", 946, 499, 90, 36),
            B("買家備註", 112, 614, 70), B("希望可以幫我打統編 12345678 測試股份有限公司 感謝", 112, 652, 400),
            B("進帳資訊", 112, 742, 70)
        };
        var note = ShopeeBatchScreens.BuyerNote(page);
        Assert.Contains("12345678", note);
        Assert.DoesNotContain("進帳資訊", note);
        var rules = new List<ChatRule> { new() { Keywords = "發票,收據,統編,統一編號,抬頭", Message = "測試訊息" } };
        Assert.Equal("統編", Assert.Single(ChatRules.Matches(note, rules)).Word);
        Assert.Empty(ChatRules.Matches("請盡快出貨", rules));
        Assert.Equal(946, ShopeeBatchScreens.ChatButton(page)!.Left);
        Assert.Equal("", ShopeeBatchScreens.BuyerNote([B("訂單編號", 112, 200, 70)]));

        var chat = new List<OcrBox>
        {
            B("網頁版聊聊", 1694, 92, 96), B("[button]", 1812, 92, 24, 24),
            B("在收到商品後，您可以從下方網址填寫【訂單編號】及【訂單金額】查詢發票資訊", 1086, 658, 330, 120),
            B("輸入文字", 990, 826, 80)
        };
        var input = ShopeeBatchScreens.ChatInput(chat)!;
        Assert.Equal(1812, ShopeeBatchScreens.ChatCollapseButton(chat)!.Left);
        // The sent message higher in the conversation is not the input's text.
        Assert.False(ShopeeBatchScreens.ChatInputHolds(chat, input, "在收到商品後，您可以從下方網址填寫"));
        var typed = new List<OcrBox> { B("在收到商品後，您可以從下方網址填寫【訂單編號】", 990, 790, 420, 60) };
        Assert.True(ShopeeBatchScreens.ChatInputHolds(typed, input, "在收到商品後，您可以從下方網址填寫"));
    }

    [Fact]
    public void Closed_chat_is_reopened_and_the_panel_is_known_open()
    {
        // Laid out like the user's 聊聊 (LOG 2026-10-15): 蝦皮 closed the conversation, no input, the
        // collapse button not in the page text; another order of the buyer in the panel.
        var chat = new List<OcrBox>
        {
            B("與買家聊聊", 1547, 84, 95), B("網頁版聊聊", 1713, 85, 78),
            B("您目前正與買家聊聊此訂單", 1011, 669, 186), B("260502AB12CDEF", 1276, 620, 107),
            B("對話已自動關閉，買家現在的新對話將由AI 賣場客服或問答小幫手接待。", 997, 826, 426),
            B("重新啟用對話", 1170, 853, 80)
        };
        Assert.Null(ShopeeBatchScreens.ChatInput(chat));
        Assert.Equal(1170, ShopeeBatchScreens.ChatReopenButton(chat)!.Left);
        Assert.True(ShopeeBatchScreens.ChatPanelOpen(chat));
        var fold = ShopeeBatchScreens.ChatCollapseButton(chat)!;
        Assert.Equal(1713 + 78 + 22, fold.Left);
        Assert.InRange(fold.Top + fold.Height / 2, 85, 105);
        Assert.False(ShopeeBatchScreens.ChatPanelOpen([B("關注", 830, 991, 98, 36), B("聊聊", 1027, 991, 90, 36)]));
    }
}
