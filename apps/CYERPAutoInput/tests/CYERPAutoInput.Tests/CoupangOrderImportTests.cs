using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>All data here is fictitious; real 酷澎 exports must never be committed.</summary>
public class CoupangOrderImportTests
{
    private static readonly string[] Header =
    [
        "編號", "捆綁配送編號", "訂單編號", "配送渠道", "快遞公司", "寄件單號", "公司商品代碼", "顯示產品名稱",
        "應開立予買家之發票金額", "應開立予酷澎之發票金額", "銷售價格", "運費類型", "運費", "數量", "選項價格", "配送訊息", "統一編號"
    ];

    private static readonly CoupangMapping Mapping = new("T01", "C0001", "酷澎訂單", "D901", "S100",
        new Dictionary<string, string> { ["新竹"] = "F3", ["黑貓"] = "F4" });

    private static string[] Row(string orderSn, string code, string invoice, string price, string shipping, string qty = "1",
        string carrier = "新竹物流", string taxId = "") =>
        ["1", "B1", orderSn, "CH", carrier, "T100", code, "測試商品", invoice, "0.0", price, "已付費", shipping, qty, price, "按電鈴", taxId];

    [Fact]
    public void Maps_a_single_item_order_with_shipping()
    {
        var result = CoupangOrderImport.Parse([Header, Row("90000000000001", "X00001", "187", "120", "67")]);
        Assert.Empty(result.Errors);
        var order = CoupangOrderImport.ToImported(Assert.Single(result.Orders), Mapping);
        Assert.Equal([new ImportedItem("X00001", "1", "120"), new ImportedItem("S100", "1", "67")], order.Items);
        var header = order.HeaderValues();
        Assert.Equal("酷澎訂單90000000000001", header["trade_note"]);
        Assert.Equal("F3", header["freight_type"]);
        Assert.Equal("187", header["cod"]);
        Assert.Equal("67", header["freight_fee"]);
        Assert.Equal("T100", header["ship_addr1"]);
        Assert.False(header.ContainsKey("tax_id"));
        Assert.Empty(order.HandoffReason);
        Assert.True(order.InvoiceEditable);
    }

    [Fact]
    public void Free_shipping_and_a_discount_give_only_the_discount_item()
    {
        var result = CoupangOrderImport.Parse([Header, Row("90000000000002", "X00002", "180", "100", "0", qty: "2")]);
        var order = CoupangOrderImport.ToImported(Assert.Single(result.Orders), Mapping);
        Assert.Equal([new ImportedItem("X00002", "2", "100"), new ImportedItem("D901", "1", "-20")], order.Items);
        Assert.False(order.HeaderValues().ContainsKey("freight_fee"));
    }

    [Fact]
    public void A_tax_id_requires_the_invoice_name()
    {
        var result = CoupangOrderImport.Parse([Header, Row("90000000000003", "X00003", "100", "100", "0", taxId: "12345678")]);
        var order = CoupangOrderImport.ToImported(Assert.Single(result.Orders), Mapping);
        Assert.Equal("12345678", order.HeaderValues()["tax_id"]);
        Assert.Contains("客戶全名", order.ValidationError());
    }

    [Fact]
    public void Hands_off_missing_codes_and_unknown_carriers()
    {
        var result = CoupangOrderImport.Parse(
        [
            Header,
            Row("90000000000004", "", "100", "100", "0"),
            Row("90000000000005", "X00005", "100", "100", "0", carrier: "某某物流")
        ]);
        Assert.Contains("沒有公司商品代碼", CoupangOrderImport.ToImported(result.Orders[0], Mapping).HandoffReason);
        Assert.Contains("沒有設定貨運別", CoupangOrderImport.ToImported(result.Orders[1], Mapping).HandoffReason);
    }

    [Fact]
    public void Leaves_out_an_order_whose_invoice_exceeds_price_plus_shipping()
    {
        var result = CoupangOrderImport.Parse([Header, Row("90000000000006", "X00006", "200", "100", "67")]);
        Assert.Empty(result.Orders);
        Assert.Contains("金額對不上", Assert.Single(result.Errors));
    }

    [Fact]
    public void Reports_a_file_without_the_coupang_header()
    {
        var result = CoupangOrderImport.Parse([["order_sn", "product_info"]]);
        Assert.Empty(result.Orders);
        Assert.Contains("酷澎", Assert.Single(result.Errors));
    }
}
