using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordsControl : UserControl
{
    private static readonly object PlaceholderRow = new();
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly DateTimePicker dateFrom = DatePicker();
    private readonly DateTimePicker dateTo = DatePicker();
    private readonly TextBox invoiceNumber = UiControls.TextBox(20);
    private readonly TextBox orderId = UiControls.TextBox(40);
    private readonly TextBox buyerName = UiControls.TextBox(200);
    private readonly TextBox buyerBan = UiControls.TextBox(10);
    private readonly ComboBox source = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox state = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NativeListViewHost recordsHost = new(9F, 24);
    private readonly Button refreshButton = new() { Text = "重新整理狀態", Width = 135, Height = 34 };
    private readonly List<InvoiceRecord> visible = [];
    private readonly Font voidedFont;
    private bool fillingRows;
    private bool selectionClearQueued;

    private ListView Records => recordsHost.List;

    public RecordsControl(LocalRepository repository, InvoiceService service)
    {
        this.repository = repository;
        this.service = service;
        voidedFont = new Font(Records.Font, FontStyle.Strikeout);
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(18);
        BuildLayout();
        ResetFilters();
        Reload();
    }

    private void BuildLayout()
    {
        source.Items.AddRange(["全部", InvoiceSources.Manual, InvoiceSources.Mo, InvoiceSources.Coupang]);
        state.Items.AddRange(["全部", InvoiceStates.Opened, InvoiceStates.Failed, InvoiceStates.Unknown, InvoiceStates.Changing, InvoiceStates.Voided]);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 3, Padding = new Padding(0, 4, 0, 6) };
        foreach (var width in new[] { 82, 0, 82, 0, 72, 0, 82, 0 })
            filters.ColumnStyles.Add(width == 0 ? new ColumnStyle(SizeType.Percent, 25) : new ColumnStyle(SizeType.Absolute, width));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        filters.Controls.Add(UiControls.Label("開立日期"), 0, 0);
        var dates = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        dates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        dates.Controls.Add(dateFrom, 0, 0);
        dates.Controls.Add(new Label { Text = "至", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = false }, 1, 0);
        dates.Controls.Add(dateTo, 2, 0);
        filters.Controls.Add(dates, 1, 0);
        filters.SetColumnSpan(dates, 3);
        AddFilter(filters, "發票號碼", invoiceNumber, 4, 0);
        AddFilter(filters, "訂單編號", orderId, 6, 0);
        AddFilter(filters, "買受人", buyerName, 0, 1);
        AddFilter(filters, "統編", buyerBan, 2, 1);
        AddFilter(filters, "來源", source, 4, 1);
        AddFilter(filters, "發票狀態", state, 6, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var query = new Button { Text = "查詢", Width = 90, Height = 34 };
        query.Click += (_, _) => Reload();
        var clear = new Button { Text = "清除條件", Width = 105, Height = 34 };
        clear.Click += (_, _) => { ResetFilters(); Reload(); };
        refreshButton.Click += async (_, _) => await RefreshFromApiAsync();
        buttons.Controls.Add(query);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(refreshButton);
        filters.Controls.Add(buttons, 0, 2);
        filters.SetColumnSpan(buttons, 8);
        ConfigureList();
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(recordsHost, 0, 1);
        Controls.Add(root);
    }

    private static void AddFilter(TableLayoutPanel panel, string label, Control field, int column, int row)
    {
        panel.Controls.Add(UiControls.Label(label), column, row);
        panel.Controls.Add(field, column + 1, row);
    }

    private void ConfigureList()
    {
        Records.Columns.Add("開立時間", 158, HorizontalAlignment.Left);
        Records.Columns.Add("發票號碼", 108, HorizontalAlignment.Left);
        Records.Columns.Add("來源", 76, HorizontalAlignment.Left);
        Records.Columns.Add("訂單編號", 155, HorizontalAlignment.Left);
        Records.Columns.Add("統編", 106, HorizontalAlignment.Left);
        Records.Columns.Add("買受人", 150, HorizontalAlignment.Left);
        Records.Columns.Add("金額", 86, HorizontalAlignment.Right);
        Records.Columns.Add("交付方式", 88, HorizontalAlignment.Left);
        Records.Columns.Add("發票狀態", 88, HorizontalAlignment.Left);
        Records.Columns.Add("上傳", 58, HorizontalAlignment.Center);
        Records.MouseDown += (_, eventArgs) =>
        {
            var hit = Records.HitTest(eventArgs.Location);
            if (hit.Item is null || ReferenceEquals(hit.Item.Tag, PlaceholderRow)) QueueClearSelection();
        };
        Records.MouseDoubleClick += (_, eventArgs) =>
        {
            var hit = Records.HitTest(eventArgs.Location);
            if (hit.Item?.Tag is InvoiceRecord record) OpenSelected(record);
        };
        recordsHost.ViewportChanged += (_, _) => { LayoutColumns(); FillPlaceholderRows(); };
    }

    public void Reload()
    {
        var updateStarted = false;
        try
        {
            visible.Clear();
            visible.AddRange(repository.Invoices.LoadOrCreate().Where(Matches));
            Records.BeginUpdate();
            updateStarted = true;
            Records.Items.Clear();
            foreach (var record in visible)
            {
                var row = NewRow([IssueTime(record), record.InvoiceNumber, record.Source, record.OrderId, record.BuyerIdentifier,
                    record.BuyerName, MoneyFormatter.Integer(record.Amount), record.Delivery, record.InvoiceState,
                    record.UploadStatus == 0 ? "" : "●"]);
                row.Tag = record;
                Records.Items.Add(row);
                StyleRecordRow(row, record);
            }
            FillPlaceholderRows();
            ClearSelection();
            LayoutColumns();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "讀取已開立發票清單失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (updateStarted) Records.EndUpdate();
        }
    }

    private bool Matches(InvoiceRecord record)
    {
        var date = ParseDate(record.InvoiceDate.Length == 0 ? record.SentAt : record.InvoiceDate);
        if (dateFrom.Checked && (date is null || date.Value.Date < dateFrom.Value.Date)) return false;
        if (dateTo.Checked && (date is null || date.Value.Date > dateTo.Value.Date)) return false;
        if (!Contains(record.InvoiceNumber, invoiceNumber.Text) || !Contains(record.OrderId, orderId.Text) ||
            !Contains(record.BuyerName, buyerName.Text) || !Contains(record.BuyerIdentifier, buyerBan.Text)) return false;
        if (source.SelectedIndex > 0 && record.Source != source.Text) return false;
        return state.SelectedIndex <= 0 || record.InvoiceState == state.Text;
    }

    private async Task RefreshFromApiAsync()
    {
        refreshButton.Enabled = false;
        refreshButton.Text = "重新整理中…";
        try
        {
            await service.RefreshAllAsync();
            Reload();
        }
        catch (PartialRefreshException error)
        {
            Reload();
            MessageBox.Show(this, error.Message, "部分資料未能更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "重新整理失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            refreshButton.Text = "重新整理狀態";
            refreshButton.Enabled = true;
        }
    }

    private void OpenSelected(InvoiceRecord record)
    {
        using var detail = new RecordDetailForm(record);
        detail.ShowDialog(FindForm());
    }

    private void FillPlaceholderRows()
    {
        if (fillingRows || Records.Columns.Count == 0) return;
        fillingRows = true;
        try
        {
            for (var index = Records.Items.Count - 1; index >= 0; index--)
                if (ReferenceEquals(Records.Items[index].Tag, PlaceholderRow)) Records.Items.RemoveAt(index);
            var capacity = Math.Max(1, recordsHost.VisibleRowCapacity());
            while (Records.Items.Count < capacity)
            {
                var row = NewRow(["", "", "", "", "", "", "", "", "", ""]);
                row.Tag = PlaceholderRow;
                Records.Items.Add(row);
                StyleRow(row);
            }
            recordsHost.SetScrollNeeded(visible.Count > capacity);
        }
        finally
        {
            fillingRows = false;
        }
    }

    private void StyleRecordRow(ListViewItem row, InvoiceRecord record)
    {
        StyleRow(row);
        if (record.UploadStatus != 0)
            row.SubItems[9].ForeColor = record.UploadStatus == 99 ? Color.FromArgb(0, 160, 72) : Color.FromArgb(215, 150, 0);
        if (record.InvoiceState != InvoiceStates.Voided) return;
        foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
        {
            subItem.ForeColor = Color.Gray;
            subItem.Font = voidedFont;
        }
    }

    private static void StyleRow(ListViewItem row)
    {
        row.UseItemStyleForSubItems = false;
        var background = row.Index % 2 == 0 ? Color.White : Color.FromArgb(238, 244, 250);
        foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
        {
            subItem.BackColor = background;
            subItem.ForeColor = SystemColors.ControlText;
        }
    }

    private void ClearSelection()
    {
        while (Records.SelectedItems.Count > 0) Records.SelectedItems[0].Selected = false;
        Records.FocusedItem = null;
    }

    private void QueueClearSelection()
    {
        if (selectionClearQueued || !Records.IsHandleCreated || Records.IsDisposed) return;
        selectionClearQueued = true;
        Records.BeginInvoke((Action)(() =>
        {
            try { ClearSelection(); }
            finally { selectionClearQueued = false; }
        }));
    }

    private void LayoutColumns()
    {
        if (Records.Columns.Count != 10 || Records.ClientSize.Width <= 0) return;
        var available = Math.Max(1, Records.ClientSize.Width - 4);
        var widths = new[] { 158, 108, 76, 155, 106, 0, 86, 88, 88, 58 };
        widths[5] = Math.Max(80, available - widths.Sum());
        var over = widths.Sum() - available;
        if (over > 0)
        {
            foreach (var index in new[] { 5, 7, 8, 6, 2, 4, 3, 0, 1 })
            {
                var minimum = index switch { 5 => 60, 3 => 110, 4 => 82, 0 => 125, 1 => 90, _ => 54 };
                var reduction = Math.Min(over, Math.Max(0, widths[index] - minimum));
                widths[index] -= reduction;
                over -= reduction;
                if (over == 0) break;
            }
        }
        else if (over < 0) widths[5] += -over;
        recordsHost.SetColumnWidths(widths);
    }

    private void ResetFilters()
    {
        var today = DateTime.Today;
        dateFrom.Value = new DateTime(today.Year, today.Month, 1);
        dateFrom.Checked = true;
        dateTo.Value = today;
        dateTo.Checked = true;
        invoiceNumber.Clear(); orderId.Clear(); buyerName.Clear(); buyerBan.Clear();
        source.SelectedIndex = 0; state.SelectedIndex = 0;
    }

    internal void VerifySmokeLayout()
    {
        if (Records.Columns.Count != 10) throw new InvalidOperationException("已開立發票原生 ListView 欄位未建立");
        if (!recordsHost.ScrollSlotReserved) throw new InvalidOperationException("已開立發票清單未保留停用垂直 scrollbar");
        if (Math.Abs(Records.Font.SizeInPoints - 9F) > 0.1F) throw new InvalidOperationException("已開立發票清單未使用 9pt 字級");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) voidedFont.Dispose();
        base.Dispose(disposing);
    }

    private static ListViewItem NewRow(IReadOnlyList<string> values)
    {
        var row = new ListViewItem(values[0]);
        for (var index = 1; index < values.Count; index++) row.SubItems.Add(values[index]);
        return row;
    }

    private static DateTimePicker DatePicker() => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd", ShowCheckBox = true, Dock = DockStyle.Fill };
    private static bool Contains(string value, string search) => search.Trim().Length == 0 || value.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private static DateTime? ParseDate(string value)
    {
        value = value.Trim();
        if (value.Length >= 10) value = value[..10];
        return DateTime.TryParseExact(value, ["yyyy/MM/dd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }
    private static string IssueTime(InvoiceRecord record) => record.InvoiceDate.Length == 0 ? record.SentAt : (record.InvoiceDate + " " + record.InvoiceTime).Trim();
}
