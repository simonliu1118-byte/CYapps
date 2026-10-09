namespace CYERPAutoInput;

internal enum OrderImportAction { None, StartSelected, RunAll }

/// <summary>
/// Preview of a parsed platform export (蝦皮, MO店+): lists the importable orders and the rows
/// that were left out, then starts one order or the batch. MO店+ orders get their tracking
/// number and invoice fields filled here, because MO店+ issues tracking numbers only after
/// the export (user, 2026-10-10).
/// </summary>
internal sealed class OrderImportForm : Form
{
    private readonly IReadOnlyList<ImportedOrder> _orders;
    private readonly Func<ImportedOrder, LedgerEntry?> _ledger;
    private readonly DataGridView _grid = new();
    private readonly bool _editable;

    public OrderImportAction Action { get; private set; }
    public ImportedOrder? SelectedOrder { get; private set; }

    public OrderImportForm(string source, string fileName, IReadOnlyList<ImportedOrder> orders, IReadOnlyList<string> errors,
        bool autoSave, Func<ImportedOrder, LedgerEntry?> ledger)
    {
        _orders = orders;
        _ledger = ledger;
        _editable = orders.Any(o => o.InvoiceEditable);
        Text = $"{source}訂單匯入";
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = _editable ? new Size(1180, 600) : new Size(860, 560);
        MinimumSize = new Size(640, 420);
        Font = new Font("Microsoft JhengHei UI", 9.5F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, _editable ? 52 : 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, errors.Count > 0 ? 110 : 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var summary = $"{fileName}：可匯入 {orders.Count} 張訂單" + (errors.Count > 0 ? $"，{errors.Count} 筆無法匯入（見下方）" : string.Empty);
        if (_editable) summary += "\n白底欄位可直接填寫：物流單號（可空白，之後回補）、有統編時必填客戶全名、發票日期、發票號碼。";
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = summary
        }, 0, 0);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 1);

        var errorBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, errors),
            Visible = errors.Count > 0
        };
        root.Controls.Add(errorBox, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4, 8, 4, 0)
        };
        var runAll = new CyPrimaryButton { Text = $"批次輸入（{orders.Count} 張）", Width = 150, Height = 34, Enabled = autoSave && orders.Count > 0 };
        runAll.Click += (_, _) => RunAll();
        var start = new CyPrimaryButton { Text = "開始輸入選取的訂單", Width = 170, Height = 34, Enabled = orders.Count > 0 };
        start.Click += (_, _) => StartSelected();
        var cancel = new CyButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(runAll);
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);
        if (!autoSave) ExplainDisabled(buttons, runAll, "批次輸入需要每張單儲存後才能接續下一張；請先在「設定」勾選自動儲存。");
        root.Controls.Add(buttons, 0, 3);
        CancelButton = cancel;

        CyVisualTheme.Apply(this);
        StyleEditableColumns();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = !_editable;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = _editable ? DataGridViewSelectionMode.CellSelect : DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        void Column(string name, string header, float weight, bool editable = false) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = !editable,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

        Column("OrderSn", "訂單編號", 130);
        if (_editable) Column("Carrier", "物流商", 110);
        Column("Tracking", "物流單號", 120, _editable);
        if (_editable)
        {
            Column("TaxId", "統編", 80);
            Column("InvoiceName", "客戶全名", 110, true);
            Column("InvoiceDate", "發票日期", 90, true);
            Column("InvoiceNo", "發票號碼", 100, true);
            Column("Total", "代收", 60);
        }
        Column("Items", "明細（品號×數量＠單價）", 260);
        Column("Handoff", "轉人工原因", 170);
        Column("Ledger", "輸入紀錄", 110);

        foreach (var order in _orders)
        {
            var index = _grid.Rows.Add();
            var row = _grid.Rows[index];
            row.Tag = order;
            row.Cells["OrderSn"].Value = order.OrderSn;
            row.Cells["Tracking"].Value = order.TrackingNumber;
            if (_editable)
            {
                row.Cells["Carrier"].Value = order.Carrier;
                row.Cells["TaxId"].Value = order.BuyerTaxId;
                row.Cells["InvoiceName"].Value = order.InvoiceName;
                row.Cells["InvoiceDate"].Value = order.InvoiceDate;
                row.Cells["InvoiceNo"].Value = order.InvoiceNo;
                row.Cells["Total"].Value = order.Total;
            }
            row.Cells["Items"].Value = order.ItemSummary();
            row.Cells["Handoff"].Value = order.HandoffReason;
            var entry = _ledger(order);
            row.Cells["Ledger"].Value = entry?.ShortText ?? string.Empty;
            if (entry?.Status == LedgerStatus.Saved) row.DefaultCellStyle.ForeColor = CyVisualTheme.TextDisabled;
        }

        _grid.CellEndEdit += (_, e) => WriteBack(e.RowIndex, e.ColumnIndex);
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].ReadOnly) StartSelected();
        };
    }

    /// <summary>Editable cells are white; read-only cells keep the theme's read-only tint.</summary>
    private void StyleEditableColumns()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
            column.DefaultCellStyle.BackColor = column.ReadOnly ? CyVisualTheme.ReadOnly : CyVisualTheme.White;
    }

    private void WriteBack(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || _grid.Rows[rowIndex].Tag is not ImportedOrder order) return;
        var value = Convert.ToString(_grid[columnIndex, rowIndex].Value)?.Trim() ?? string.Empty;
        switch (_grid.Columns[columnIndex].Name)
        {
            case "Tracking": order.TrackingNumber = value; break;
            case "InvoiceName": order.InvoiceName = value; break;
            case "InvoiceDate":
                if (InputRules.TryNormalizeValidDate(value, out var digits))
                {
                    value = InputRules.FormatDateForDisplay(digits);
                    _grid[columnIndex, rowIndex].Value = value;
                }
                order.InvoiceDate = value;
                break;
            case "InvoiceNo": order.InvoiceNo = value; break;
        }
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

    private bool CommitEdits()
    {
        if (_grid.IsCurrentCellInEditMode && !_grid.EndEdit()) return false;
        if (_grid.CurrentCell is { } cell) WriteBack(cell.RowIndex, cell.ColumnIndex);
        return true;
    }

    private bool ShowProblem(ImportedOrder order, string problem)
    {
        if (problem.Length == 0) return false;
        var index = _orders.ToList().IndexOf(order);
        if (index >= 0) _grid.CurrentCell = _grid.Rows[index].Cells[0];
        MessageBox.Show(this, $"訂單 {order.OrderSn}：{problem}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    private void StartSelected()
    {
        if (!CommitEdits()) return;
        var index = _grid.CurrentCell?.RowIndex ?? -1;
        if (index < 0 || index >= _orders.Count) return;
        var order = _orders[index];
        if (ShowProblem(order, order.ValidationError())) return;
        SelectedOrder = order;
        Action = OrderImportAction.StartSelected;
        DialogResult = DialogResult.OK;
    }

    private void RunAll()
    {
        if (!CommitEdits()) return;
        // Orders the batch will skip anyway (handed off, already saved) need no fixing here.
        foreach (var order in _orders.Where(o => o.HandoffReason.Length == 0 && _ledger(o)?.Status != LedgerStatus.Saved))
            if (ShowProblem(order, order.ValidationError())) return;
        Action = OrderImportAction.RunAll;
        DialogResult = DialogResult.OK;
    }
}
