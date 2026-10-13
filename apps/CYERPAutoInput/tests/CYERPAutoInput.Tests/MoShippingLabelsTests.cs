using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>MO店+ 寄件單 PDF text → tracking numbers. Every number here is made up.</summary>
public class MoShippingLabelsTests
{
    // Laid out like the PDF text: per label the barcode, the shipper half with 訂單編號, then the
    // receipt half that prints the barcode and 宅配單號 again.
    private static string Label(string tracking, string order) =>
        $"D{tracking}D寄件日/2026/01/02取件所/客戶單號 90000000000001內容物 TP00000000000001 001識別碼: 1234訂單編號:{order}" +
        $"D{tracking}D宅配單號{tracking}客戶單號90000000000001客戶代碼1111111111宅配單號{tracking}";

    private static MoOrder Order(string sn, string tracking = "") =>
        new(2, sn, [new MoOrderLine("A1", 1, 100, 100, "x")], "第三方物流-新竹貨運", tracking, "", "", "", "", 100);

    [Fact]
    public void Pairs_each_order_with_its_label()
    {
        var file = MoShippingLabels.Parse("label.pdf", [Label("1000000001", "90000000000011") + Label("1000000002", "90000000000012")]);
        Assert.Equal(MoPdfKind.Labels, file.Kind);
        Assert.Empty(file.Problems);
        Assert.Equal([("90000000000011", "1000000001"), ("90000000000012", "1000000002")], file.Labels);
    }

    [Fact]
    public void Recognises_the_pickup_summary()
    {
        var file = MoShippingLabels.Parse("data.pdf", ["momo第三方物流到廠取貨總表 NO 宅配單號 出貨單號 1 1000000001 90000000000001"]);
        Assert.Equal(MoPdfKind.Summary, file.Kind);
        var (orders, messages) = MoShippingLabels.Apply([Order("90000000000011")], [file]);
        Assert.Equal("", orders[0].TrackingNumber);
        Assert.Contains(messages, m => m.Contains("到廠取貨總表"));
    }

    [Fact]
    public void Unrelated_pdf_is_skipped()
    {
        var file = MoShippingLabels.Parse("other.pdf", ["發票 123"]);
        Assert.Equal(MoPdfKind.Unknown, file.Kind);
        Assert.Equal(MoPdfKind.Image, MoShippingLabels.Parse("family.pdf", [" "]).Kind);
    }

    [Fact]
    public void Fills_tracking_and_lists_what_does_not_match()
    {
        var file = MoShippingLabels.Parse("label.pdf", [Label("1000000001", "90000000000011") + Label("1000000003", "90000000000099")]);
        var (orders, messages) = MoShippingLabels.Apply([Order("90000000000011"), Order("90000000000012")], [file]);
        Assert.Equal("1000000001", orders[0].TrackingNumber);
        Assert.Equal("", orders[1].TrackingNumber);
        Assert.Contains(messages, m => m.Contains("90000000000099") && m.Contains("不在匯出檔"));
        Assert.Contains(messages, m => m.Contains("90000000000012") && m.Contains("沒有寄件單"));
    }

    [Fact]
    public void Conflicting_numbers_are_never_guessed()
    {
        var a = MoShippingLabels.Parse("a.pdf", [Label("1000000001", "90000000000011")]);
        var b = MoShippingLabels.Parse("b.pdf", [Label("1000000002", "90000000000011")]);
        var (orders, messages) = MoShippingLabels.Apply([Order("90000000000011"), Order("90000000000012", "2000000000")], [a, b,
            MoShippingLabels.Parse("c.pdf", [Label("1000000005", "90000000000012")])]);
        Assert.Equal("", orders[0].TrackingNumber);
        Assert.Contains(messages, m => m.Contains("不同的宅配單號"));
        Assert.Equal("2000000000", orders[1].TrackingNumber);
        Assert.Contains(messages, m => m.Contains("保留匯出檔"));
    }

    [Fact]
    public void Barcode_without_order_is_reported()
    {
        var file = MoShippingLabels.Parse("label.pdf", ["D1000000001D宅配單號1000000001 寄件日"]);
        Assert.Empty(file.Labels);
        Assert.Contains(file.Problems, p => p.Contains("1000000001") && p.Contains("找不到訂單編號"));
    }
}
