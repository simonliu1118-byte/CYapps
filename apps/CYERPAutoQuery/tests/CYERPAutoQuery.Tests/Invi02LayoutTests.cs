using System.Drawing;
using Xunit;

namespace CYERPAutoQuery.Tests;

public class Invi02LayoutTests
{
    [Theory]
    [InlineData("品號資料建立作業(INVI02)[志遠醫療器材行]", true)]
    [InlineData("品號資料建立作業(invi02)", true)]
    [InlineData("銷貨單建立作業(COPI08)[志遠醫療器材行]", false)]
    [InlineData("品號資料建立作業", false)]
    [InlineData("INVI02", false)]
    public void Invi02TitleNeedsNameAndCode(string title, bool expected) =>
        Assert.Equal(expected, Invi02Layout.IsInvi02Title(title));

    [Theory]
    [InlineData("鼎新電腦Conductor [ccliu-測試]", true)]
    [InlineData("品號資料建立作業(INVI02)[志遠醫療器材行]", false)]
    [InlineData("銷貨單建立作業(COPI08)[志遠醫療器材行]", false)]
    public void ErpMainWindowTitle(string title, bool expected) =>
        Assert.Equal(expected, Invi02Layout.IsErpMainTitle(title));

    [Fact]
    public void QueryDialogTitle() =>
        Assert.True(Invi02Layout.IsQueryDialogTitle("設定查詢條件"));

    [Theory]
    [InlineData("TMessageForm", true)]
    [InlineData("#32770", true)]
    [InlineData("TfrmInvi02", false)]
    public void MessageDialogClasses(string cls, bool expected) =>
        Assert.Equal(expected, Invi02Layout.IsMessageDialogClass(cls));

    [Fact]
    public void Invi02TabCentersMatchMeasuredScreenshots()
    {
        // Screenshots 2026-10-16: tab text centers at 56 + 79 * i from the page's left edge.
        var page = new Rectangle(100, 300, 1200, 300);
        Assert.Equal(new Point(156, 312), Invi02Layout.Invi02Tabs.PointFor(page, "基本資料1"));
        Assert.Equal(new Point(235, 312), Invi02Layout.Invi02Tabs.PointFor(page, "基本資料2"));
        Assert.Equal(new Point(314, 312), Invi02Layout.Invi02Tabs.PointFor(page, "採購生管1"));
        Assert.Equal(new Point(472, 312), Invi02Layout.Invi02Tabs.PointFor(page, "售價"));
    }

    [Fact]
    public void QueryDialogTabCenters()
    {
        var page = new Rectangle(10, 20, 470, 400);
        Assert.Equal(new Point(60, 32), Invi02Layout.QueryDialogTabs.PointFor(page, "一般選項"));
        Assert.Equal(new Point(126, 32), Invi02Layout.QueryDialogTabs.PointFor(page, "進階設定"));
    }

    [Fact]
    public void ExportFieldTabCenters()
    {
        var page = new Rectangle(712, 436, 490, 280);
        Assert.Equal(new Point(772, 448), Invi02Layout.ExportFieldTabs.PointFor(page, "單頭"));
        Assert.Equal(new Point(862, 448), Invi02Layout.ExportFieldTabs.PointFor(page, "單身"));
    }

    [Fact]
    public void TabPointIsNullWhenUnknownOrOutsideThePage()
    {
        Assert.Null(Invi02Layout.Invi02Tabs.PointFor(new Rectangle(0, 0, 1200, 300), "不存在"));
        Assert.Null(Invi02Layout.Invi02Tabs.PointFor(new Rectangle(0, 0, 300, 300), "售價"));
    }

    [Fact]
    public void ExportTabHasAGeometry() =>
        Assert.Equal(0, Invi02Layout.Invi02Tabs.IndexOf(Invi02Layout.ExportTab));

    [Theory]
    [InlineData(@"C:\Users\User\DocumentsFile\INVI02_2.XLSX", true)]
    [InlineData(@"D:\CY\TEMP\INVI02_1.XLSX", true)]
    [InlineData("Excel 2007以上 試算表(*.xlsx)", false)]
    [InlineData("", false)]
    [InlineData("MB001", false)]
    public void FilePaths(string text, bool expected) =>
        Assert.Equal(expected, Invi02Layout.LooksLikeFilePath(text));

    [Theory]
    [InlineData("TDBEdit", true)]
    [InlineData("TcxDBTextEdit", true)]
    [InlineData("TcxDBImageComboBox", true)]
    [InlineData("TDBMemo", true)]
    [InlineData("TFDBCheckBox", true)]
    [InlineData("TcxButton", false)]
    [InlineData("TPanel", false)]
    public void InputLikeClasses(string cls, bool expected) =>
        Assert.Equal(expected, Invi02Layout.IsInputLike(cls));

    [Fact]
    public void ComboIsNotAnEditBox()
    {
        Assert.True(Invi02Layout.IsCombo("TcxComboBox"));
        Assert.False(Invi02Layout.IsEditBox("TcxComboBox"));
        Assert.True(Invi02Layout.IsEditBox("TcxTextEdit"));
    }

    [Theory]
    [InlineData("確定(&O)", "確定(O)")]
    [InlineData(" 一般選項 ", "一般選項")]
    [InlineData("發票資料（一）", "發票資料(一)")]
    public void Captions(string raw, string expected) =>
        Assert.Equal(expected, Invi02Layout.NormalizeCaption(raw));

    [Fact]
    public void RowsGroupByCenterAndSortLeftToRight()
    {
        // INVI02 header (screenshot): 品號 版次 單位 / 品名 / 規格 (+ 定重 checkbox, 3 px higher).
        var items = new (string Name, Rectangle Rect)[]
        {
            ("單位", new Rectangle(477, 160, 46, 22)),
            ("品號", new Rectangle(87, 160, 228, 22)),
            ("版次", new Rectangle(380, 160, 54, 22)),
            ("規格", new Rectangle(87, 212, 335, 22)),
            ("定重", new Rectangle(478, 211, 12, 12)),
            ("品名", new Rectangle(87, 186, 335, 22)),
        };
        var rows = Invi02Layout.BuildRows(items, i => i.Rect);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["品號", "版次", "單位"], rows[0].Select(i => i.Name));
        Assert.Equal(["品名"], rows[1].Select(i => i.Name));
        Assert.Equal(["規格", "定重"], rows[2].Select(i => i.Name));
    }

    [Theory]
    [InlineData("TApplication", false)]  // Delphi's hidden window with the same caption (V0.4.0 LOG)
    [InlineData("TMessageForm", false)]
    [InlineData("#32770", false)]
    [InlineData("TfrmMain", true)]
    public void MainWindowClassSkipsHiddenApplicationWindow(string className, bool expected) =>
        Assert.Equal(expected, Invi02Layout.IsErpMainClass(className));

    [Theory]
    [InlineData("人數已滿!!", true)]
    [InlineData(" 人數 已滿 ", true)]
    [InlineData("報表沒有資料", false)]
    public void SeatsFullMessageIsRecognised(string text, bool expected) =>
        Assert.Equal(expected, Invi02Layout.IsSeatsFullMessage(text));

    [Fact]
    public void SeatsFullIsReportedWithItsOwnCode() =>
        Assert.Equal(ErpAgentErrorCode.ERP_SEATS_FULL,
            ErpAgentProtocol.Classify(new QueryFailure(ErpAgentErrorCode.ERP_SEATS_FULL, "ERP 人數已滿")));

    [Theory]
    [InlineData("使用者因閒置而登出授權，請問是否重新登入授權?\nYes: 重新登入授權\nNo: 不重新登入授權，請關閉所有程式並離開ERP系統", true, true)]
    [InlineData("使用者因閒置而登出授權，無法開啟作業程式!", true, false)]
    [InlineData("報表沒有資料", false, false)]
    public void IdleLogoutBoxesAreRecognised(string text, bool logout, bool question)
    {
        Assert.Equal(logout, Invi02Layout.IsIdleLogoutMessage(text));
        Assert.Equal(question, Invi02Layout.IsRelicenseQuestion(text));
    }
}
