using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>
/// MO店+ export reading and mapping. All data is fictitious: the .xls fixtures are built by
/// Fixtures/make_mo_fixture.py; real MO店+ exports must never be committed.
/// </summary>
public class MoOrderImportTests
{
    private const string FixturePassword = "fixture-pass-0001";

    private static readonly MoMapping Mapping = new("T01", "C0001", "MO店+訂單", "D901", "S100",
        MoOrderImport.ParseFreightTable("7-11=F1\n全家=F2\n新竹=F3"));

    private static List<string[]> ReadFixture(string name, string? password = FixturePassword)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return XlsReader.ReadFirstSheet(stream, password);
    }

    private static Dictionary<string, ImportedOrder> ImportFixture()
    {
        var result = MoOrderImport.Parse(ReadFixture("mo_export_40bit.xls"));
        Assert.Empty(result.Errors);
        return result.Orders.Select(o => MoOrderImport.ToImported(o, Mapping)).ToDictionary(o => o.OrderSn);
    }

    [Theory]
    [InlineData("mo_export_40bit.xls")]
    [InlineData("mo_export_128bit.xls")]
    public void Reads_a_password_protected_export(string name)
    {
        var rows = ReadFixture(name);
        Assert.Equal(68, rows.Count);
        Assert.Equal("訂單編號", rows[0][0]);
        Assert.Equal("90000000000001", rows[1][0]);
        Assert.Equal("001", rows[1][1]);
        // Long names in the filler rows push the shared strings past one record (CONTINUE).
        Assert.Equal("測試規格" + new string('長', 40) + "59", rows[67][21]);
    }

    [Fact]
    public void Rejects_a_wrong_or_missing_password()
    {
        Assert.Contains("密碼不正確", Assert.Throws<InvalidDataException>(() => ReadFixture("mo_export_40bit.xls", "wrong")).Message);
        Assert.Contains("密碼保護", Assert.Throws<InvalidDataException>(() => ReadFixture("mo_export_40bit.xls", null)).Message);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_xls()
    {
        Assert.Throws<InvalidDataException>(() => XlsReader.ReadFirstSheet(new MemoryStream(new byte[1024]), null));
    }

    [Fact]
    public void Adds_discount_and_shipping_items_and_fills_the_header()
    {
        var order = ImportFixture()["90000000000001"];
        Assert.Equal(
        [
            new ImportedItem("X00001", "1", "500"),
            new ImportedItem("D901", "1", "-20"),
            new ImportedItem("S100", "1", "45")
        ], order.Items);
        var header = order.HeaderValues();
        Assert.Equal("T01", header["order_type"]);
        Assert.Equal("C0001", header["customer_code"]);
        Assert.Equal("MO店+訂單90000000000001", header["trade_note"]);
        Assert.Equal("F1", header["freight_type"]);
        Assert.Equal("525", header["cod"]);
        Assert.Equal("45", header["freight_fee"]);
        Assert.Equal("T0000001", header["ship_addr1"]);
        Assert.Empty(order.HandoffReason);
    }

    [Fact]
    public void Merges_rows_of_the_same_item_and_skips_zero_shipping()
    {
        var order = ImportFixture()["90000000000002"];
        Assert.Equal([new ImportedItem("X00002", "2", "100"), new ImportedItem("D901", "1", "-2")], order.Items);
        var header = order.HeaderValues();
        Assert.False(header.ContainsKey("freight_fee"));
        Assert.False(header.ContainsKey("ship_addr1")); // tracking number filled later
        Assert.Equal("F2", header["freight_type"]);
        Assert.Equal("198", header["cod"]);
    }

    [Fact]
    public void Hands_off_combination_codes_missing_codes_notes_and_unknown_carriers()
    {
        var orders = ImportFixture();
        Assert.Contains("組合品號", orders["90000000000003"].HandoffReason);
        Assert.Contains("沒有商品原廠編號", orders["90000000000004"].HandoffReason);
        Assert.Contains("店家備註：請先出貨", orders["90000000000005"].HandoffReason);
        Assert.Contains("不在貨運別對照表", orders["90000000000006"].HandoffReason);
        Assert.False(orders["90000000000006"].HeaderValues().ContainsKey("freight_type"));
    }

    [Fact]
    public void A_buyer_tax_id_requires_the_invoice_name()
    {
        var order = ImportFixture()["90000000000005"];
        Assert.Equal("12345678", order.HeaderValues()["tax_id"]);
        Assert.Contains("客戶全名", order.ValidationError());
        order.InvoiceName = "測試公司";
        order.InvoiceDate = "2026/10/01";
        order.InvoiceNo = "AB00000001";
        Assert.Empty(order.ValidationError());
        var header = order.HeaderValues();
        Assert.Equal("測試公司", header["inv_name"]);
        Assert.Equal("2026/10/01", header["inv_date"]);
        Assert.Equal("AB00000001", header["inv_no"]);
        order.InvoiceDate = "2026/13/01";
        Assert.Contains("發票日期", order.ValidationError());
    }

    private static readonly string[] Header =
    [
        "訂單編號", "訂單編號(序號)", "商品原廠編號", "數量", "商品售價", "物流商", "物流單號",
        "客人支付運費", "平台補貼運費", "商品滿額免運費",
        "開立發票金額_依品項(若您是自行開立發票，請依此金額開立予消費者)", "開立發票金額加總(若您是自行開立發票，請依此金額開立予消費者)"
    ];

    [Fact]
    public void Leaves_out_an_order_whose_shipping_does_not_reconcile()
    {
        var result = MoOrderImport.Parse(
        [
            Header,
            ["90000000000010", "001", "X00010", "1", "100", "7-11店出", "", "45", "0", "0", "100", "150"],
            ["90000000000011", "001", "X00011", "1", "100", "7-11店出", "", "45", "-45", "0", "90", "90"]
        ]);
        var order = Assert.Single(result.Orders);
        Assert.Equal("90000000000011", order.OrderSn);
        Assert.Equal(10m, order.Discount);
        Assert.Contains(result.Errors, e => e.Contains("90000000000010") && e.Contains("運費對不上"));
    }

    [Fact]
    public void Leaves_out_an_order_whose_invoice_exceeds_the_list_price()
    {
        var result = MoOrderImport.Parse(
        [
            Header,
            ["90000000000012", "001", "X00012", "1", "100", "7-11店出", "", "0", "0", "0", "120", "120"]
        ]);
        Assert.Empty(result.Orders);
        Assert.Contains("折價券", Assert.Single(result.Errors));
    }

    [Fact]
    public void Reports_a_file_without_the_MO_header()
    {
        var result = MoOrderImport.Parse([["order_sn", "product_info"]]);
        Assert.Empty(result.Orders);
        Assert.Contains("MO店+", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("7-11店出", "F1")]
    [InlineData("全家店出", "F2")]
    [InlineData("第三方物流-新竹貨運", "F3")]
    [InlineData("黑貓宅急便", "")]
    public void Matches_the_freight_type_by_keyword(string carrier, string expected)
    {
        Assert.Equal(expected, MoOrderImport.MatchFreightType(carrier, Mapping.FreightTypes));
    }

    [Fact]
    public void The_longest_matching_keyword_wins()
    {
        var table = MoOrderImport.ParseFreightTable("新竹=F3\n新竹貨運=F5\n不完整的行\n=F9");
        Assert.Equal(2, table.Count);
        Assert.Equal("F5", MoOrderImport.MatchFreightType("第三方物流-新竹貨運", table));
    }
}
