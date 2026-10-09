using System.IO.Compression;
using System.Text;
using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>All data here is fictitious; real Shopee exports must never be committed.</summary>
public class ShopeeOrderImportTests
{
    private static readonly string[] Header = ["tracking_number", "order_sn", "product_info", "remark_from_buyer", "seller_note"];

    private const string TwoItems =
        "[1] 商品名稱:測試商品甲 §特價§; 商品選項名稱:紅色 (20入); 價格: $ 350; 數量: 1; 商品選項貨號: X00001; " +
        "[2] 商品名稱:測試商品乙; 商品選項名稱:藍色; 價格: $ 1,200; 數量: 3; 商品選項貨號: X00002; ";

    [Fact]
    public void Parses_an_order_with_several_items()
    {
        var result = ShopeeOrderImport.Parse([Header, ["TW000000000001X", "2601010TEST001", TwoItems, "", ""]]);
        Assert.Empty(result.Errors);
        var order = Assert.Single(result.Orders);
        Assert.Equal("2601010TEST001", order.OrderSn);
        Assert.Equal("TW000000000001X", order.TrackingNumber);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(new ShopeeOrderItem("X00001", "1", "350", "測試商品甲 §特價§"), order.Items[0]);
        Assert.Equal(new ShopeeOrderItem("X00002", "3", "1200", "測試商品乙"), order.Items[1]);
    }

    [Fact]
    public void Remarks_mark_the_order_for_handoff_before_the_details()
    {
        var result = ShopeeOrderImport.Parse([Header,
            ["T1", "A1", TwoItems, "", ""],
            ["T2", "A2", TwoItems, "請改寄", ""],
            ["T3", "A3", TwoItems, "", "補發"],
            ["T4", "A4", TwoItems, "請改寄", "補發"]]);
        Assert.Empty(result.Errors);
        Assert.Equal("", result.Orders[0].HandoffReason);
        Assert.Equal("買家備註：請改寄", result.Orders[1].HandoffReason);
        Assert.Equal("賣家備註：補發", result.Orders[2].HandoffReason);
        Assert.Equal("買家備註：請改寄；賣家備註：補發", result.Orders[3].HandoffReason);
    }

    [Fact]
    public void Remark_columns_are_optional()
    {
        var order = Assert.Single(ShopeeOrderImport.Parse([["tracking_number", "order_sn", "product_info"], ["T1", "A1", TwoItems]]).Orders);
        Assert.Equal("", order.HandoffReason);
    }

    [Fact]
    public void Maps_header_values_from_the_local_settings()
    {
        var order = new ShopeeOrder(2, "2601010TEST001", "TW000000000001X", []);
        var values = ShopeeOrderImport.HeaderValues(order, new ShopeeMapping("T01", "C0001", "蝦皮訂單"));
        Assert.Equal("T01", values["order_type"]);
        Assert.Equal("C0001", values["customer_code"]);
        Assert.Equal("蝦皮訂單2601010TEST001", values["trade_note"]);
        Assert.Equal("TW000000000001X", values["ship_addr1"]);
    }

    [Fact]
    public void An_order_with_an_unusable_item_is_reported_and_left_out()
    {
        var noCode = "[1] 商品名稱:測試; 價格: $ 100; 數量: 1; 商品選項貨號: ; ";
        var badQty = "[1] 商品名稱:測試; 價格: $ 100; 數量: 0; 商品選項貨號: X00003; ";
        var result = ShopeeOrderImport.Parse([Header, ["T1", "A1", noCode], ["T2", "A2", badQty], ["T3", "A3", TwoItems]]);
        Assert.Equal("A3", Assert.Single(result.Orders).OrderSn);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("A1", result.Errors[0]);
        Assert.Contains("A2", result.Errors[1]);
    }

    [Fact]
    public void Duplicate_orders_and_blank_rows_are_handled()
    {
        var result = ShopeeOrderImport.Parse([Header, ["T1", "A1", TwoItems], ["", "", ""], ["T1", "A1", TwoItems]]);
        Assert.Single(result.Orders);
        Assert.Contains("重複", Assert.Single(result.Errors));
    }

    [Fact]
    public void Missing_columns_are_reported()
    {
        var result = ShopeeOrderImport.Parse([["order_sn", "something"], ["A1", "x"]]);
        Assert.Empty(result.Orders);
        Assert.Contains("tracking_number", Assert.Single(result.Errors));
    }

    [Fact]
    public void Reads_shared_and_inline_strings_from_an_xlsx_whose_sheet_is_not_sheet1()
    {
        using var stream = BuildXlsx();
        var rows = XlsxReader.ReadFirstSheet(stream);
        Assert.Equal(["tracking_number", "order_sn", "product_info"], rows[0]);
        Assert.Equal("TW000000000001X", rows[1][0]);
        Assert.Equal("2601010TEST001", rows[1][1]);
        Assert.Equal(TwoItems, rows[1][2]);

        var order = Assert.Single(ShopeeOrderImport.Parse(rows).Orders);
        Assert.Equal(2, order.Items.Count);
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("C7", 2)]
    [InlineData("Z1", 25)]
    [InlineData("AA3", 26)]
    public void Column_index_from_cell_reference(string reference, int index) =>
        Assert.Equal(index, XlsxReader.ColumnIndex(reference));

    private static MemoryStream BuildXlsx()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Add("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"orders\" sheetId=\"2\" r:id=\"rId9\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId9\" Type=\"worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
            Add("xl/sharedStrings.xml", $"<sst xmlns=\"{ns}\"><si><t>tracking_number</t></si><si><t>order_sn</t></si><si><r><t>product_</t></r><r><t>info</t></r></si><si><t>2601010TEST001</t></si></sst>");
            var info = System.Security.SecurityElement.Escape(TwoItems);
            Add("xl/worksheets/sheet2.xml", $"<worksheet xmlns=\"{ns}\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c></row>" +
                $"<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>TW000000000001X</t></is></c><c r=\"B2\" t=\"s\"><v>3</v></c><c r=\"C2\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{info}</t></is></c></row>" +
                "</sheetData></worksheet>");
        }
        stream.Position = 0;
        return stream;
    }
}
