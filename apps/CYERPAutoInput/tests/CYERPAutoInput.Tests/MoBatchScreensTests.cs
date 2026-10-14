using Xunit;

namespace CYERPAutoInput.Tests;

public class MoBatchScreensTests
{
    private static OcrBox B(string text, int left, int top, int width = 0, int height = 20) =>
        new(text, left, top, width > 0 ? width : Math.Max(14, text.Length * 14), height);

    [Fact]
    public void Reads_my_orders_and_its_filters()
    {
        List<OcrBox> page =
        [
            B("店+管理系統", 540, 30), B("登出", 1500, 20),
            B("我的訂單", 60, 175), B("批次出貨", 60, 215), B("退貨/退款", 60, 255),
            B("未出貨(2)", 258, 205), B("未付款", 368, 205), B("配送中(10)", 455, 205),
            B("處理提醒", 258, 274), B("全部", 342, 274), B("未延遲(2)", 405, 274),
            B("配送方式", 258, 331), B("全部", 342, 331), B("第三方物流(1)", 405, 331), B("全家店出(1)", 711, 331),
            B("匯出報表", 1777, 485)
        ];
        Assert.True(MoBatchScreens.SignedIn(page));
        Assert.True(MoBatchScreens.IsOrderManagement(page));
        Assert.Equal(2, MoBatchScreens.Tab(page, "未出貨")!.Value.Count);
        Assert.Equal(10, MoBatchScreens.Tab(page, "配送中")!.Value.Count);
        Assert.Equal(0, MoBatchScreens.Tab(page, "未付款")!.Value.Count);
        Assert.Equal(274, MoBatchScreens.Filter(page, "處理提醒", "全部")!.Top);
        Assert.Equal(331, MoBatchScreens.Filter(page, "配送方式", "全部")!.Top);
        Assert.False(MoBatchScreens.SignedIn([B("登入", 100, 100), B("帳號", 100, 140)]));
    }

    [Fact]
    public void Lists_the_ship_methods_with_orders()
    {
        List<OcrBox> page =
        [
            B("配送方式", 258, 343),
            B("第三方物流-富昇物流", 330, 327), B("第三方物流-新竹貨運(1)", 494, 327), B("7-11店出", 980, 327), B("7-11倉出", 1072, 327),
            B("全家店出(1)", 1166, 327), B("全家冷凍店出(2)", 1362, 327), B("自訂物流", 1598, 327)
        ];
        var methods = MoBatchScreens.ShipMethods(page);
        Assert.Equal(["第三方物流-新竹貨運", "全家店出", "全家冷凍店出"], methods.Select(m => m.Name));
        Assert.Equal([MoShipKind.Hct, MoShipKind.Family, MoShipKind.Other], methods.Select(m => m.Kind));
        // The count as a piece of its own.
        List<OcrBox> pieces = [B("配送方式", 258, 343), B("全家店出", 1166, 327, 60), B("(1)", 1228, 327, 20), B("全家倉出", 1272, 327, 60)];
        Assert.Equal(["全家店出"], MoBatchScreens.ShipMethods(pieces).Select(m => m.Name));
        Assert.Equal(MoShipKind.Seven, MoBatchScreens.Kind("7-11店出"));
        Assert.Equal(MoShipKind.Other, MoBatchScreens.Kind("7-11倉出"));
    }

    [Fact]
    public void Selects_and_ships_a_method()
    {
        List<OcrBox> list = [B("批次出貨", 60, 215), B("已選 1 個訂單", 305, 509), B("批次出貨", 400, 509), B("訂單編號: 66101400799232", 545, 614)];
        Assert.Equal(1, MoBatchScreens.Selected(list)!.Value.Count);
        // In pieces (user's LOG, 2026-10-14).
        List<OcrBox> pieces = [B("已選", 305, 640, 30), B("0", 337, 640, 10), B("個訂單", 349, 640, 44), B("批次出貨", 401, 640, 78)];
        Assert.Equal(0, MoBatchScreens.Selected(pieces)!.Value.Count);
        Assert.Equal(401, MoBatchScreens.ShipSelectedButton(pieces)!.Left);
        Assert.Equal(400, MoBatchScreens.ShipSelectedButton(list)!.Left);

        List<OcrBox> ship = [B("批次出貨(共1筆訂單)", 65, 231), B("訂單1: 66101400799232", 155, 273), B("併箱出貨", 512, 273), B("確認出貨", 863, 790)];
        Assert.Equal(1, MoBatchScreens.ShipPageCount(ship));
        Assert.Equal(["66101400799232"], MoBatchScreens.ShipPageOrders(ship));
        Assert.Equal(["66101400797534"], MoBatchScreens.ShipPageOrders([B("訂單1:", 163, 402, 50), B("66101400797534", 215, 402, 105), B("共 2 件未出貨商品", 348, 402)]));
        Assert.Null(MoBatchScreens.ShipPageCount(list));
        Assert.NotNull(MoBatchScreens.ShippingDialogClose([B("批次出貨中", 728, 77), B("關閉", 990, 214), B("查看批次出貨記錄", 1062, 214)]));
    }

    [Fact]
    public void Reads_the_newest_ship_record()
    {
        List<OcrBox> page =
        [
            B("批次出貨記錄(僅查詢最近30天記錄)", 252, 249),
            B("出貨時間", 268, 301), B("配送方式", 492, 301), B("出貨訂單數", 696, 301), B("狀態", 1510, 301), B("操作", 1640, 301),
            B("2026-10-14 16:42:39", 268, 344), B("第三方物流-新竹貨運", 492, 344), B("1", 698, 344), B("1", 900, 344), B("-", 1104, 344), B("-", 1308, 344),
            B("全部成功", 1510, 344), B("列印文件", 1640, 344),
            B("2026-10-13 16:36:13", 268, 382), B("第三方物流-新竹貨運", 492, 382), B("2", 698, 382), B("2", 900, 382), B("全部成功", 1510, 382), B("列印文件", 1640, 382)
        ];
        var record = MoBatchScreens.LatestRecord(page)!;
        Assert.Equal("2026-10-14 16:42:39", record.Time);
        Assert.Equal("第三方物流-新竹貨運", record.Method);
        Assert.Equal((1, 1, "全部成功"), (record.Orders, record.Shipped, record.Status));
        Assert.Equal(344, record.PrintButton!.Top);
        Assert.True(MoBatchScreens.RecordMatches(record, MoShipKind.Hct, 1));
        Assert.False(MoBatchScreens.RecordMatches(record, MoShipKind.Family, 1));
        Assert.True(MoBatchScreens.RecordMatches(record with { Method = "全家店到店" }, MoShipKind.Family, 1));
        Assert.True(MoBatchScreens.RecordMatches(record with { Method = "7-11店到店" }, MoShipKind.Seven, 1));

        // The row as one text, with the button beside it.
        List<OcrBox> whole =
        [
            B("出貨時間", 268, 301),
            new("2026-10-14 19:05:58 全家店到店 1 1 - - 全部成功 列印文件", 252, 338, 1600, 38), B("列印文件", 1646, 348),
            new("2026-10-14 16:42:39 第三方物流-新竹貨運 1 1 - - 全部成功 列印文件", 252, 376, 1600, 38)
        ];
        var top = MoBatchScreens.LatestRecord(whole)!;
        Assert.Equal(("2026-10-14 19:05:58", "全家店到店", 1, "全部成功"), (top.Time, top.Method, top.Orders, top.Status));
        Assert.Equal(1646, top.PrintButton!.Left);
        // A whole cell as the link: clicked on its text at the left.
        var cell = MoBatchScreens.OnText(new OcrBox("列印文件", 1640, 338, 200, 38));
        Assert.True(cell.CenterX < 1680);
        Assert.Equal(56, MoBatchScreens.OnText(B("列印文件", 1646, 348, 56)).Width);
        Assert.True(MoBatchScreens.RecordMatches(top, MoShipKind.Family, 1, new DateTime(2026, 10, 14, 19, 5, 58)));
        Assert.False(MoBatchScreens.RecordMatches(top, MoShipKind.Family, 1, new DateTime(2026, 10, 14, 19, 10, 0)));

        List<OcrBox> dialog = [B("注意", 646, 87), B("列印標籤", 663, 232), B("列印明細", 769, 232), B("列印出貨總表", 875, 232)];
        Assert.Equal(875, MoBatchScreens.PrintDocumentButton(dialog, "列印出貨總表")!.Left);
        Assert.True(MoBatchScreens.ExportDone([B("匯出提醒", 222, 68), B("1~2筆，狀態：已完成", 240, 254), B("關閉", 790, 330)]));
    }
}
