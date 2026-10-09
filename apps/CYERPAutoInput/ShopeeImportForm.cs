namespace CYERPAutoInput;

internal enum ShopeeImportAction { None, StartSelected, RunAll }

/// <summary>
/// Preview of a parsed Shopee export: lists the importable orders and the rows that were
/// left out, then either loads one order into the form or starts the batch.
/// </summary>
internal sealed class ShopeeImportForm : Form
{
    private readonly IReadOnlyList<ShopeeOrder> _orders;
    private readonly DataGridView _grid = new();

    public ShopeeImportAction Action { get; private set; }
    public ShopeeOrder? SelectedOrder { get; private set; }

    public ShopeeImportForm(string fileName, ShopeeImportResult result, bool autoSave)
    {
        _orders = result.Orders;
        Text = "蝦皮訂單匯入";
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = new Size(860, 560);
        MinimumSize = new Size(640, 420);
        Font = new Font("Microsoft JhengHei UI", 9.5F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, result.Errors.Count > 0 ? 110 : 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"{fileName}：可匯入 {result.Orders.Count} 張訂單" + (result.Errors.Count > 0 ? $"，{result.Errors.Count} 筆無法匯入（見下方）" : string.Empty)
        }, 0, 0);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 1);

        var errors = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, result.Errors),
            Visible = result.Errors.Count > 0
        };
        root.Controls.Add(errors, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4, 8, 4, 0)
        };
        var runAll = new CyPrimaryButton { Text = $"批次輸入（{_orders.Count} 張）", Width = 150, Height = 34, Enabled = autoSave && _orders.Count > 0 };
        runAll.Click += (_, _) =>
        {
            Action = ShopeeImportAction.RunAll;
            DialogResult = DialogResult.OK;
        };
        var start = new CyPrimaryButton { Text = "開始輸入選取的訂單", Width = 170, Height = 34, Enabled = _orders.Count > 0 };
        start.Click += (_, _) => StartSelected();
        var cancel = new CyButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(runAll);
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);
        if (!autoSave) ExplainDisabled(buttons, runAll, "批次輸入需要每張單儲存後才能接續下一張；請先在「設定」勾選自動儲存。");
        root.Controls.Add(buttons, 0, 3);
        CancelButton = cancel;

        CyVisualTheme.Apply(this);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "訂單編號", FillWeight = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "物流單號", FillWeight = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "品項", FillWeight = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "明細（品號×數量＠單價）", FillWeight = 300 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "備註（有則轉人工）", FillWeight = 200 });
        foreach (var order in _orders)
        {
            var summary = string.Join("、", order.Items.Select(i => $"{i.ItemCode}×{i.Quantity}＠{i.UnitPrice}"));
            _grid.Rows.Add(order.OrderSn, order.TrackingNumber, order.Items.Count, summary, order.HandoffReason);
        }
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) StartSelected(); };
    }

    /// <summary>
    /// A disabled button gets no mouse messages, so its explanation is shown from the
    /// container when the pointer is over the button's area.
    /// </summary>
    private static void ExplainDisabled(Control host, Control button, string text)
    {
        var tip = new ToolTip();
        var shown = false;
        host.MouseMove += (_, e) =>
        {
            var over = button.Bounds.Contains(e.Location);
            if (over && !shown) { tip.Show(text, host, button.Left, button.Top - 44, 6000); shown = true; }
            else if (!over && shown) { tip.Hide(host); shown = false; }
        };
        host.MouseLeave += (_, _) => { tip.Hide(host); shown = false; };
        host.Disposed += (_, _) => tip.Dispose();
    }

    private void StartSelected()
    {
        var index = _grid.CurrentRow?.Index ?? -1;
        if (index < 0 || index >= _orders.Count) return;
        SelectedOrder = _orders[index];
        Action = ShopeeImportAction.StartSelected;
        DialogResult = DialogResult.OK;
    }
}
