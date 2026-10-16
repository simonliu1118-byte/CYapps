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
    public void TabPointIsNullWhenUnknownOrOutsideThePage()
    {
        Assert.Null(Invi02Layout.Invi02Tabs.PointFor(new Rectangle(0, 0, 1200, 300), "不存在"));
        Assert.Null(Invi02Layout.Invi02Tabs.PointFor(new Rectangle(0, 0, 300, 300), "售價"));
    }

    [Fact]
    public void EveryReadTabHasAGeometry() =>
        Assert.All(Invi02Layout.ReadTabs, t => Assert.True(Invi02Layout.Invi02Tabs.IndexOf(t) >= 0));

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
}
