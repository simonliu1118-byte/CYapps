namespace CYERPAutoQuery;

/// <summary>
/// Shows everything read for one 品號: 品號／品名／規格 on top, then every field of the header
/// and the four tabs by position, so the fields to keep can be chosen from real readings.
/// </summary>
internal sealed class ResultForm : Form
{
    public ResultForm(ItemQueryResult result)
    {
        Text = $"查詢結果 - {result.ItemCode}";
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        ClientSize = new Size(760, 600);
        MinimumSize = new Size(560, 400);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 12, 14, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var summary = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSummary(summary, "品號", result.ItemCode);
        AddSummary(summary, "品名", result.ItemName);
        AddSummary(summary, "規格", result.Specification);
        root.Controls.Add(summary, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        };
        grid.Columns.Add("group", "頁籤");
        grid.Columns.Add("position", "位置");
        grid.Columns.Add("value", "內容");
        grid.Columns.Add("type", "控制項");
        grid.Columns[0].Width = 90;
        grid.Columns[1].Width = 110;
        grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.Columns[3].Width = 150;
        foreach (var f in result.Fields)
            grid.Rows.Add(f.Group, f.Position, f.Value, f.ControlClass);
        root.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        var close = new CyPrimaryButton { Text = "關閉", Width = 92, Height = 34, DialogResult = DialogResult.OK };
        var copy = new CyButton { Text = "複製全部", Width = 110, Height = 34 };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(result.ToTsv()); }
            catch (Exception ex) { MessageBox.Show(this, $"複製失敗：{ex.Message}", Text); }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 10, 12, 0),
            Text = $"共 {result.Fields.Count} 欄，耗時 {result.Elapsed.TotalSeconds:0.0} 秒"
        });
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        CyVisualTheme.Apply(this);
    }

    private static void AddSummary(TableLayoutPanel table, string label, string value)
    {
        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 4, 12, 4), Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold) }, 0, row);
        table.Controls.Add(new TextBox { Text = value, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 4) }, 1, row);
    }
}
