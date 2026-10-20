namespace CYERPAutoQuery;

/// <summary>
/// A 銷貨單／銷退單 opened from a 未收款 憑證 (user, 2026-10-17: see what the order sold without
/// leaving the 未收款 list). A separate window; the main window keeps its result.
/// </summary>
internal sealed class OrderDetailForm : Form
{
    private static string Num(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    public OrderDetailForm(SalesOrder order)
    {
        Text = $"{order.Kind}單 {order.OrderNo}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 620);
        MinimumSize = new Size(700, 420);
        Font = new Font("Microsoft JhengHei UI", 9.5F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        Controls.Add(layout);

        var header = Grid();
        header.Columns.Add("name", "欄位");
        header.Columns.Add("value", "內容");
        header.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (var (name, value) in new[]
        {
            ("類別", order.Kind), ("單號", order.OrderNo), ("單據日期", order.Date.ToString("yyyy/MM/dd")),
            ("客戶", $"{order.CustomerCode} {order.CustomerName}"), ("業務員", order.Salesperson), ("部門", order.Department),
            ("付款條件", order.PaymentTerm), ("課稅別", order.TaxType), ("總數量", Num(order.TotalQuantity)),
            ("金額", Num(order.Amount)), ("稅額", Num(order.Tax)), ("合計", Num(order.Total)),
            ("備註", order.Note), ("銷退原因", order.Reason),
        })
            if (value.Trim().Length > 0) header.Rows.Add(name, value);
        layout.Controls.Add(header, 0, 0);

        var lines = Grid();
        foreach (var c in new[] { "序號", "品號", "品名", "規格", "數量", "單位", "單價", "金額", "類型", "贈/備品量", "庫別", "批號", "備註", "原銷貨單" })
            lines.Columns.Add(c, c);
        foreach (var l in order.Lines)
            lines.Rows.Add(l.Seq, l.ItemCode, l.ItemName, l.Specification, Num(l.Quantity), l.Unit, Num(l.UnitPrice),
                Num(l.Amount), l.GiftType, Num(l.GiftQuantity), l.Warehouse, l.Batch, l.Note, l.SourceOrder);
        layout.Controls.Add(lines, 0, 1);
        CyVisualTheme.Apply(this);
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
    };
}
