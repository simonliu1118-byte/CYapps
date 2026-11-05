using CYInvoice.Core;

namespace CYInvoice.WinForms;

internal sealed class BusinessDetailControl : UserControl
{
    private readonly TableLayoutPanel sections = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };

    public BusinessDetailControl(IEnumerable<KeyValuePair<string,string>> fields,
        IEnumerable<BusinessDetailTable>? tables = null)
    {
        Font=new Font("Microsoft JhengHei UI",10F);
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(8);
        var rows = fields.Select(row => (IReadOnlyList<string>)new[] { row.Key, string.IsNullOrWhiteSpace(row.Value) ? "－" : row.Value }).ToArray();
        AddTable(new("基本資料", ["項目", "內容"], rows), metadata: true);
        foreach (var table in tables ?? []) AddTable(table, metadata: false);
        Controls.Add(sections);
    }

    private void AddTable(BusinessDetailTable table, bool metadata)
    {
        if (!metadata) AddSection(new Label { Text = table.Title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 12, 0, 4) });
        var grid = UiControls.Grid();
        grid.Tag = table.Title;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var label = table.Columns[index];
            var column = new DataGridViewTextBoxColumn { Name = "column" + index, HeaderText = label, Width = metadata && index == 0 ? 126 : label.Contains("單號") ? 128 : label.Contains("日期") ? 96 : 80 };
            if (!metadata && (label.Contains("金額") || label.Contains("單價") || label.Contains("數量") || label.Contains("稅額")))
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns.Add(column);
        }
        var flexible = metadata ? 1 : 0;
        if (grid.Columns.Count > flexible) { grid.Columns[flexible].MinimumWidth = metadata ? 180 : 110; UiControls.FitGridColumns(grid, flexible); }
        foreach (var row in table.Rows) grid.Rows.Add(row.Cast<object>().ToArray());
        grid.Dock = DockStyle.Top;
        grid.Margin = new Padding(0, 0, 0, 4);
        // One document scrolls vertically; tables do not hide fields in nested scroll areas.
        grid.ScrollBars = ScrollBars.None;
        void SizeRows()
        {
            if (grid.IsDisposed) return;
            grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
            grid.Height = grid.ColumnHeadersHeight + grid.Rows.Cast<DataGridViewRow>().Sum(row => row.Height) + 4;
        }
        grid.SizeChanged += (_, _) => SizeRows();
        grid.HandleCreated += (_, _) => SizeRows();
        AddSection(grid);
    }

    private void AddSection(Control control)
    {
        var row = sections.RowCount++;
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        sections.Controls.Add(control, 0, row);
    }

    internal static BusinessDetailTable Products(string title, IReadOnlyList<InvoiceItem> items) => new(title,
        ["品名", "數量", "單價", "金額"], items.Select(item => (IReadOnlyList<string>)new[] {
            item.Description, item.QuantityDecimal.Length > 0 ? item.QuantityDecimal : item.Quantity.ToString(),
            item.UnitPriceDecimal.Length > 0 ? item.UnitPriceDecimal : item.UnitPrice.ToString(),
            item.AmountDecimal.Length > 0 ? item.AmountDecimal : item.Amount.ToString() }).ToArray());
}
