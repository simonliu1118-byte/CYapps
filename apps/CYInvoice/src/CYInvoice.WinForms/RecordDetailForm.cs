using CYInvoice.Core;

namespace CYInvoice.WinForms;

internal sealed class RecordDetailForm : Form
{
    public RecordDetailForm(InvoiceRecord record)
    {
        Text = $"發票詳細資訊－{record.InvoiceNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 620);
        MinimumSize = new Size(760, 520);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 7 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField(fields, 0, "開立時間", IssueTime(record), "發票號碼", record.InvoiceNumber);
        AddField(fields, 1, "來源", record.Source, "訂單編號", record.OrderId);
        AddField(fields, 2, "統一編號", record.BuyerIdentifier, "買受人", record.BuyerName);
        AddField(fields, 3, "發票金額", MoneyFormatter.Integer(record.Amount), "交付方式", record.Delivery);
        AddField(fields, 4, "發票狀態", record.InvoiceState, "上傳狀態", record.UploadStatusText);
        AddField(fields, 5, "使用環境", record.Environment == Environments.Production ? "正式" : "測試", "最後確認", record.LastChecked);
        AddField(fields, 6, "錯誤訊息", record.ErrorMessage, "總備註", record.MainRemark);
        var items = UiControls.Grid();
        items.ReadOnly = true;
        items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        items.Columns.Add(Column("品名", 300, fill: true));
        items.Columns.Add(Column("數量", 100, right: true));
        items.Columns.Add(Column("單價", 130, right: true));
        items.Columns.Add(Column("金額", 130, right: true));
        foreach (var item in record.Items)
        {
            var values = InvoiceCalculator.ItemDecimals(item);
            items.Rows.Add(item.Description, values.Quantity.ToString(), MoneyFormatter.Decimal(values.UnitPrice.ToString()), MoneyFormatter.Decimal(values.Amount.ToString()));
        }
        items.ClearSelection();
        var close = new Button { Text = "關閉", DialogResult = DialogResult.OK, Width = 110, Height = 34, Anchor = AnchorStyles.None };
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(items, 0, 1);
        root.Controls.Add(close, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }

    private static DataGridViewTextBoxColumn Column(string title, int width, bool right = false, bool fill = false) => new()
    {
        HeaderText = title, Width = width, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };

    private static void AddField(TableLayoutPanel panel, int row, string label1, string value1, string label2, string value2)
    {
        panel.Controls.Add(UiControls.Label(label1), 0, row);
        panel.Controls.Add(Value(value1), 1, row);
        panel.Controls.Add(UiControls.Label(label2), 2, row);
        panel.Controls.Add(Value(value2), 3, row);
    }

    private static TextBox Value(string value) => new() { Text = value, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(3, 4, 8, 4) };
    private static string IssueTime(InvoiceRecord record) => record.InvoiceDate.Length == 0 ? record.SentAt : (record.InvoiceDate + " " + record.InvoiceTime).Trim();
}
