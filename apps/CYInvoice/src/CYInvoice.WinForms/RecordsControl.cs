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
    private readonly DataGridView grid = UiControls.Grid();
    private readonly Button refreshButton = new() { Text = "重新整理狀態", Width = 135, Height = 34 };
    private readonly List<InvoiceRecord> visible = [];
    private bool updatingPlaceholders;
    private bool clearingSelection;

    public RecordsControl(LocalRepository repository, InvoiceService service)
    {
        this.repository = repository;
        this.service = service;
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
        dates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        dates.Controls.Add(dateFrom, 0, 0);
        dates.Controls.Add(UiControls.Label("～", ContentAlignment.MiddleCenter), 1, 0);
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
        ConfigureGrid();
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(grid, 0, 1);
        Controls.Add(root);
    }

    private static void AddFilter(TableLayoutPanel panel, string label, Control field, int column, int row)
    {
        panel.Controls.Add(UiControls.Label(label), column, row);
        panel.Controls.Add(field, column + 1, row);
    }

    private void ConfigureGrid()
    {
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AddColumn("開立時間", 150);
        AddColumn("發票號碼", 105);
        AddColumn("來源", 90);
        AddColumn("訂單編號", 165);
        AddColumn("統編", 118);
        AddColumn("買受人", 150);
        AddColumn("金額", 90, right: true);
        AddColumn("交付方式", 95);
        AddColumn("發票狀態", 95);
        AddColumn("上傳", 58, center: true);
        UiControls.ReserveVerticalScrollBar(grid, 5);
        grid.CellDoubleClick += (_, eventArgs) => OpenSelected(eventArgs.RowIndex);
        grid.CellFormatting += FormatCell;
        grid.SelectionChanged += (_, _) => ClearPlaceholderSelection();
        grid.SizeChanged += (_, _) => EnsurePlaceholderRows();
    }

    private void AddColumn(string title, int width, bool right = false, bool center = false)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title, Width = width, MinimumWidth = Math.Min(width, 58), SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : center ? DataGridViewContentAlignment.MiddleCenter : DataGridViewContentAlignment.MiddleLeft },
        });
    }

    public void Reload()
    {
        try
        {
            visible.Clear();
            visible.AddRange(repository.Invoices.LoadOrCreate().Where(Matches));
            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (var record in visible)
            {
                grid.Rows.Add(IssueTime(record), record.InvoiceNumber, record.Source, record.OrderId, record.BuyerIdentifier,
                    record.BuyerName, MoneyFormatter.Integer(record.Amount), record.Delivery, record.InvoiceState,
                    record.UploadStatus == 0 ? "" : "●");
            }
            EnsurePlaceholderRows();
            grid.ClearSelection();
            grid.ResumeLayout();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "讀取已開立發票清單失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private void OpenSelected(int index)
    {
        if (index < 0 || index >= visible.Count) return;
        using var detail = new RecordDetailForm(visible[index]);
        detail.ShowDialog(FindForm());
    }

    private void EnsurePlaceholderRows()
    {
        if (updatingPlaceholders || grid.ColumnCount == 0 || grid.ClientSize.Height <= grid.ColumnHeadersHeight) return;
        updatingPlaceholders = true;
        try
        {
            for (var index = grid.Rows.Count - 1; index >= visible.Count; index--)
            {
                if (ReferenceEquals(grid.Rows[index].Tag, PlaceholderRow)) grid.Rows.RemoveAt(index);
            }

            var availableHeight = Math.Max(0, grid.ClientSize.Height - grid.ColumnHeadersHeight - 2);
            var visibleRowCapacity = availableHeight / Math.Max(1, grid.RowTemplate.Height);
            while (grid.Rows.Count < visibleRowCapacity)
            {
                var index = grid.Rows.Add();
                grid.Rows[index].Tag = PlaceholderRow;
                grid.Rows[index].ReadOnly = true;
            }
        }
        finally
        {
            updatingPlaceholders = false;
        }
    }

    private void ClearPlaceholderSelection()
    {
        if (clearingSelection || grid.CurrentCell is null || grid.CurrentCell.RowIndex < visible.Count) return;
        clearingSelection = true;
        grid.ClearSelection();
        grid.CurrentCell = null;
        clearingSelection = false;
    }

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= visible.Count) return;
        var record = visible[eventArgs.RowIndex];
        if (eventArgs.ColumnIndex == 9 && record.UploadStatus != 0)
            eventArgs.CellStyle.ForeColor = record.UploadStatus == 99 ? Color.FromArgb(0, 160, 72) : Color.FromArgb(215, 150, 0);
        if (record.InvoiceState == InvoiceStates.Voided)
        {
            eventArgs.CellStyle.ForeColor = Color.Gray;
            eventArgs.CellStyle.Font = new Font(grid.Font, FontStyle.Strikeout);
        }
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
