namespace CYERPAutoInput;

internal enum OrderImportAction { None, StartSelected, RunAll }

/// <summary>
/// Preview of a parsed platform export (蝦皮, MO店+, 酷澎): lists the importable orders and the
/// rows that were left out; the ticked orders are entered, one alone or as a batch (user,
/// 2026-10-13: one button, a tick box per row). An order already saved by this program is shown
/// as such and cannot be ticked; one with a reason for a person is red and not ticked at first.
/// What happened is the batch's closing list, not columns here. MO店+ orders get their invoice
/// fields filled here (user, 2026-10-10).
/// </summary>
internal sealed class OrderImportForm : Form
{
    private readonly IReadOnlyList<ImportedOrder> _orders;
    private readonly Func<ImportedOrder, LedgerEntry?> _ledger;
    private readonly DataGridView _grid = new();
    private readonly bool _editable;

    public OrderImportAction Action { get; private set; }
    public ImportedOrder? SelectedOrder { get; private set; }

    /// <summary>The ticked orders, in list order, for <see cref="OrderImportAction.RunAll"/>.</summary>
    public IReadOnlyList<ImportedOrder> SelectedOrders { get; private set; } = [];

    private readonly bool _autoSave;
    private readonly CyPrimaryButton _start = new() { Width = 170, Height = 34 };

    public OrderImportForm(string source, string fileName, IReadOnlyList<ImportedOrder> orders, IReadOnlyList<string> errors,
        bool autoSave, Func<ImportedOrder, LedgerEntry?> ledger, Func<string, Task<string>>? lookupName = null,
        IReadOnlyList<string>? notes = null)
    {
        _orders = orders;
        _autoSave = autoSave;
        // Orders for a person are named below too, so the reason is there without hovering.
        notes = [.. orders.Where(o => o.HandoffReason.Length > 0).Select(o => $"訂單 {o.OrderSn} 需人工處理（預設不勾）：{o.HandoffReason}"),
            .. notes ?? []];
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, _editable ? 52 : 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, errors.Count + notes.Count > 0 ? 110 : 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var summary = $"{fileName}：可匯入 {orders.Count} 張訂單" + (errors.Count > 0 ? $"，{errors.Count} 筆無法匯入（見下方）" : string.Empty)
            + (notes.Count > 0 ? $"，{notes.Count} 項要注意（見下方）" : string.Empty)
            + "。勾選要輸入的訂單後按「開始輸入」（勾一張單獨輸入，多張為批次）。";
        if (_editable) summary += "\n白底欄位可直接填寫：物流單號（可空白，之後回補）、客戶全名（有統編時自動查詢經濟部登記名稱）、發票日期、發票號碼。";
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
            Text = string.Join(Environment.NewLine, errors.Concat(notes)),
            Visible = errors.Count + notes.Count > 0
        };
        root.Controls.Add(errorBox, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4, 8, 4, 0)
        };
        _start.Click += (_, _) => Start();
        var cancel = new CyButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_start);
        buttons.Controls.Add(cancel);
        UpdateStart();
        root.Controls.Add(buttons, 0, 3);
        CancelButton = cancel;

        CyVisualTheme.Apply(this);
        if (lookupName is not null) Shown += async (_, _) => await FillInvoiceNamesAsync(lookupName);
    }

    /// <summary>Fills 客戶全名 of orders with a tax ID from the registry; a miss leaves it blank.</summary>
    private async Task FillInvoiceNamesAsync(Func<string, Task<string>> lookupName)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not ImportedOrder { MissingInvoiceName: true } order) continue;
            var name = await lookupName(order.BuyerTaxId);
            if (IsDisposed || name.Length == 0 || !order.MissingInvoiceName) continue;
            if (_grid.IsCurrentCellInEditMode && _grid.CurrentCell?.RowIndex == row.Index &&
                _grid.CurrentCell.OwningColumn.Name == "InvoiceName") continue; // the user is typing it
            order.InvoiceName = name;
            row.Cells["InvoiceName"].Value = name;
        }
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false; // the tick boxes; the other columns are read-only one by one
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = _editable ? DataGridViewSelectionMode.CellSelect : DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        // Column widths and row heights are fixed (user, 2026-10-11).
        _grid.AllowUserToResizeColumns = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        void Column(string name, string header, float weight, int minWidth, bool editable = false) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = weight,
                MinimumWidth = minWidth,
                ReadOnly = !editable,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Pick",
            HeaderText = "輸入",
            ToolTipText = "勾選要輸入的訂單；按這裡全選或全不選",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 76,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        Column("OrderSn", "訂單編號", 140, 128);
        if (_editable) Column("Carrier", "物流商", 100, 70);
        Column("Tracking", "物流單號", 120, 90, _editable);
        if (_editable)
        {
            Column("TaxId", "統編", 80, 76);
            Column("InvoiceName", "客戶全名", 110, 80, true);
            Column("InvoiceDate", "發票日期", 90, 84, true);
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "InvoiceDatePick",
                HeaderText = string.Empty,
                ToolTipText = "選擇發票日期",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 28,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            Column("InvoiceNo", "發票號碼", 100, 90, true);
            Column("Total", "代收", 55, 50);
        }
        Column("Items", "明細（品號×數量＠單價）", 260, 120);
        _grid.ShowCellToolTips = true; // truncated details and reasons show in full on hover

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
            var entry = _ledger(order);
            if (entry?.Status == LedgerStatus.Saved)
            {
                // Entered and saved before: shown, never entered again.
                row.Cells["Pick"].Value = false;
                row.Cells["Pick"].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = CyVisualTheme.TextDisabled;
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = "已輸入過，不能再次輸入：" + entry.Describe();
                foreach (DataGridViewCell cell in row.Cells) if (cell.OwningColumn.Name != "Pick") cell.ReadOnly = true;
            }
            else
            {
                row.Cells["Pick"].Value = order.HandoffReason.Length == 0;
                var tip = string.Join("\n", new[]
                {
                    order.HandoffReason.Length > 0 ? "需人工處理：" + order.HandoffReason : string.Empty,
                    entry is not null ? entry.Describe() : string.Empty
                }.Where(t => t.Length > 0));
                if (order.HandoffReason.Length > 0) row.DefaultCellStyle.ForeColor = CyVisualTheme.Danger;
                if (tip.Length > 0)
                    foreach (DataGridViewCell cell in row.Cells) if (cell.OwningColumn.Name is "OrderSn" or "Items" or "Pick") cell.ToolTipText = tip;
            }
        }

        // A tick counts at once, not when the cell is left.
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.CurrentCell?.OwningColumn.Name == "Pick" && _grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Pick") UpdateStart(); };
        _grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (_grid.Columns[e.ColumnIndex].Name != "Pick") return;
            var open = _grid.Rows.Cast<DataGridViewRow>().Where(r => !r.Cells["Pick"].ReadOnly).ToList();
            var tick = open.Any(r => r.Cells["Pick"].Value is not true);
            foreach (var r in open) r.Cells["Pick"].Value = tick;
            UpdateStart();
        };
        // Already entered: the tick box gives way to the words.
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Pick" || e.Graphics is null) return;
            if (!_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            TextRenderer.DrawText(e.Graphics, "已輸入過", _grid.Font, e.CellBounds, CyVisualTheme.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            e.Handled = true;
        };

        _grid.CellFormatting += TintReadOnlyCells;
        _grid.CellEndEdit += (_, e) => WriteBack(e.RowIndex, e.ColumnIndex);
        if (!_editable) return;

        // 發票日期 gets the slashes while typing, like the main form's date fields.
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not TextBox box) return;
            box.TextChanged -= FormatDateWhileTyping;
            if (_grid.CurrentCell?.OwningColumn.Name == "InvoiceDate") box.TextChanged += FormatDateWhileTyping;
        };
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "InvoiceDatePick" || e.Graphics is null) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            CalendarButton.DrawGlyph(e.Graphics, e.CellBounds, CyVisualTheme.TextSecondary);
            e.Handled = true;
        };
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "InvoiceDatePick") return;
            var dateColumn = _grid.Columns["InvoiceDate"]!.Index;
            var cell = _grid.GetCellDisplayRectangle(dateColumn, e.RowIndex, false);
            var rowIndex = e.RowIndex;
            DatePickerPopup.Show(_grid, cell, Convert.ToString(_grid[dateColumn, rowIndex].Value) ?? string.Empty, date =>
            {
                _grid[dateColumn, rowIndex].Value = date;
                WriteBack(rowIndex, dateColumn);
            });
        };
    }

    /// <summary>
    /// Editable cells are white and read-only cells tinted, set per cell at paint time so the
    /// theme's row styles cannot override it.
    /// </summary>
    private void TintReadOnlyCells(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (!_editable || e.ColumnIndex < 0 || e.CellStyle is null || _grid.Columns[e.ColumnIndex].Name == "Pick") return;
        e.CellStyle.BackColor = _grid.Columns[e.ColumnIndex].ReadOnly ? CyVisualTheme.ReadOnly : CyVisualTheme.White;
    }

    private static void FormatDateWhileTyping(object? sender, EventArgs e)
    {
        if (sender is not TextBox box) return;
        var formatted = InputRules.FormatDateForDisplay(InputRules.NormalizeDateDigits(box.Text));
        if (box.Text == formatted) return;
        box.Text = formatted;
        box.SelectionStart = box.Text.Length;
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

    private List<ImportedOrder> Ticked() =>
        _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Cells["Pick"].Value is true && !r.Cells["Pick"].ReadOnly && r.Tag is ImportedOrder)
            .Select(r => (ImportedOrder)r.Tag!).ToList();

    private void UpdateStart()
    {
        var count = Ticked().Count;
        _start.Text = count > 1 ? $"開始輸入（{count} 張）" : "開始輸入";
        _start.Enabled = count > 0;
    }

    /// <summary>One ticked order is entered alone; two or more are a batch, which saves each one.</summary>
    private void Start()
    {
        if (!CommitEdits()) return;
        var ticked = Ticked();
        if (ticked.Count == 0) return;
        if (ticked.Count == 1)
        {
            StartSelected(ticked[0]);
            return;
        }
        if (!_autoSave)
        {
            MessageBox.Show(this, $"勾了 {ticked.Count} 張：批次輸入需要每張單儲存後才能接續下一張，請先在「設定」勾選自動儲存；或只勾一張輸入。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        RunAll(ticked);
    }

    private void StartSelected(ImportedOrder order)
    {
        if (ShowProblem(order, order.ValidationError())) return;
        if (order.MissingInvoiceName && order.HandoffReason.Length == 0 &&
            MessageBox.Show(this, $"訂單 {order.OrderSn} 有統一編號，但沒有發票的客戶全名（經濟部查無或未填）。\n\n建議先填寫客戶全名；仍要輸入嗎？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        SelectedOrder = order;
        Action = OrderImportAction.StartSelected;
        DialogResult = DialogResult.OK;
    }

    private void RunAll(List<ImportedOrder> ticked)
    {
        // Orders the batch will skip anyway (handed off) need no fixing here.
        var toEnter = ticked.Where(o => o.HandoffReason.Length == 0).ToList();
        foreach (var order in toEnter)
            if (ShowProblem(order, order.ValidationError())) return;
        var noName = toEnter.Where(o => o.MissingInvoiceName).Select(o => o.OrderSn).ToList();
        if (noName.Count > 0 &&
            MessageBox.Show(this, $"以下 {noName.Count} 張有統一編號，但沒有發票的客戶全名（經濟部查無或未填）：\n{string.Join("、", noName)}\n\n建議先填寫；仍要開始批次輸入嗎？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        SelectedOrders = ticked;
        Action = OrderImportAction.RunAll;
        DialogResult = DialogResult.OK;
    }
}
