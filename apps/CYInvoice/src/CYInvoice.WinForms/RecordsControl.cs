using System.Globalization;
using System.Runtime.InteropServices;
using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordsControl : UserControl
{
    private const string CopyHintText = "單擊發票號碼即可複製";
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
    private readonly NativeListViewHost recordsHost = new(10F, 22);
    private readonly Button refreshButton = UiControls.StandardButton("重新整理狀態");
    private readonly Label copyHint = new()
    {
        Text = CopyHintText,
        AutoSize = true,
        ForeColor = Color.DimGray,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(12, 10, 0, 0),
    };
    private readonly System.Windows.Forms.Timer copyFeedbackTimer = new() { Interval = 2000 };
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
        Font = new Font("Microsoft JhengHei UI", 12F);
        copyHint.Font = new Font(Font.FontFamily, 10F);
        copyFeedbackTimer.Tick += (_, _) => ResetCopyHint();
        BuildLayout();
        ResetFilters();
        Reload();
    }

    private void BuildLayout()
    {
        source.Items.AddRange(["全部", InvoiceSources.Manual, InvoiceSources.Mo, InvoiceSources.Coupang, InvoiceSources.Digiwin]);
        state.Items.AddRange(["全部", InvoiceStates.Opened, InvoiceStates.Failed, InvoiceStates.Unknown, InvoiceStates.Changing, InvoiceStates.Voided]);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 3, Padding = new Padding(0, 4, 0, 6) };
        foreach (var width in new[] { 94, 0, 90, 0, 84, 0, 94, 0 })
            filters.ColumnStyles.Add(width == 0 ? new ColumnStyle(SizeType.Percent, 25) : new ColumnStyle(SizeType.Absolute, width));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
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
        var query = UiControls.StandardButton("查詢");
        query.Click += (_, _) => Reload();
        var clear = UiControls.StandardButton("清除條件");
        clear.Click += (_, _) => { ResetFilters(); Reload(); };
        refreshButton.Click += async (_, _) => await RefreshFromApiAsync();
        buttons.Controls.Add(query);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(refreshButton);
        buttons.Controls.Add(copyHint);
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
        Records.OwnerDraw = true;
        Records.DrawColumnHeader += (_, eventArgs) => NativeListViewHost.DrawHeader(eventArgs, Records.Font);
        Records.DrawItem += (_, eventArgs) => { if (Records.View != View.Details) eventArgs.DrawDefault = true; };
        Records.DrawSubItem += DrawRecordSubItem;
        Records.MouseDown += (_, eventArgs) =>
        {
            var hit = Records.HitTest(eventArgs.Location);
            if (hit.Item is null || ReferenceEquals(hit.Item.Tag, PlaceholderRow)) QueueClearSelection();
        };
        Records.MouseClick += CopyInvoiceNumberIfRequested;
        Records.MouseDoubleClick += (_, eventArgs) =>
        {
            var hit = Records.HitTest(eventArgs.Location);
            if (hit.Item?.Tag is InvoiceRecord record) OpenSelected(record);
        };
        recordsHost.ViewportChanged += (_, _) => { FillPlaceholderRows(); LayoutColumns(); };
    }

    public void Reload()
    {
        var updateStarted = false;
        try
        {
            visible.Clear();
            var currentEnvironment = repository.Settings.LoadOrCreate().Environment;
            visible.AddRange(repository.Invoices.LoadOrCreate()
                .Where(record => record.Environment == currentEnvironment)
                .Where(Matches));
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
            ResetCopyHint();
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

    private void CopyInvoiceNumberIfRequested(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left) return;
        var hit = Records.HitTest(eventArgs.Location);
        if (hit.Item?.Tag is not InvoiceRecord record || hit.SubItem is null) return;
        if (hit.Item.SubItems.IndexOf(hit.SubItem) != 1) return;
        var number = record.InvoiceNumber.Trim();
        if (number.Length == 0) return;
        try
        {
            Clipboard.SetText(number);
            ShowCopyFeedback($"✓ 已複製 {number}", success: true);
        }
        catch (ExternalException)
        {
            ShowCopyFeedback("複製失敗，請再試一次", success: false);
        }
    }

    private void ShowCopyFeedback(string text, bool success)
    {
        copyFeedbackTimer.Stop();
        copyHint.Text = text;
        copyHint.ForeColor = success ? Color.FromArgb(0, 145, 70) : Color.Firebrick;
        copyFeedbackTimer.Start();
    }

    private void ResetCopyHint()
    {
        copyFeedbackTimer.Stop();
        copyHint.Text = CopyHintText;
        copyHint.ForeColor = Color.DimGray;
    }

    private void OpenSelected(InvoiceRecord record)
    {
        using var detail = new RecordDetailForm(record, repository, service);
        if (string.Equals(record.Delivery, InvoiceService.DeliveryPaper, StringComparison.Ordinal))
        {
            detail.MinimumSize = new Size(760, 620);
            detail.ClientSize = new Size(800, 700);
        }
        else
        {
            detail.MinimumSize = new Size(880, 600);
            detail.ClientSize = new Size(980, 660);
        }
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
        if (record.InvoiceState == InvoiceStates.Voided)
        {
            for (var index = 0; index < 9; index++)
            {
                row.SubItems[index].ForeColor = Color.Gray;
                row.SubItems[index].Font = voidedFont;
            }
        }

        var upload = row.SubItems[9];
        upload.Font = Records.Font;
        upload.ForeColor = record.UploadStatus switch
        {
            99 => Color.FromArgb(0, 160, 72),
            91 => Color.FromArgb(196, 0, 0),
            0 => SystemColors.ControlText,
            _ => Color.FromArgb(215, 150, 0),
        };
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
        var available = recordsHost.ColumnViewportWidth;
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

    private void DrawRecordSubItem(object? sender, DrawListViewSubItemEventArgs eventArgs)
    {
        if (eventArgs.Item is null || eventArgs.SubItem is null) return;
        using (var brush = new SolidBrush(eventArgs.SubItem.BackColor))
            eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= eventArgs.ColumnIndex == 6
            ? TextFormatFlags.Right
            : eventArgs.ColumnIndex == 9 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
        var textBounds = Rectangle.Inflate(eventArgs.Bounds, -5, 0);
        TextRenderer.DrawText(eventArgs.Graphics, eventArgs.SubItem.Text, eventArgs.SubItem.Font ?? Records.Font,
            textBounds, eventArgs.SubItem.ForeColor, flags);
        using var pen = new Pen(Color.FromArgb(190, 190, 190));
        eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Top, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Bottom);
        eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
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
        if (!recordsHost.UsesOnlyNativeScrollBar) throw new InvalidOperationException("已開立發票清單仍含額外 scrollbar 控制項");
        if (Math.Abs(Records.Font.SizeInPoints - 10F) > 0.1F) throw new InvalidOperationException("已開立發票清單未使用 10pt 字級");
        if (Records.Items.Count == 0 || Records.GetItemRect(0).Height > 24)
            throw new InvalidOperationException($"已開立發票清單資料列過高：{(Records.Items.Count == 0 ? 0 : Records.GetItemRect(0).Height)}px");
        if (!UiControls.HasLogicalSize(refreshButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("已開立發票清單按鈕未使用標準尺寸");
        if (copyHint.Text != CopyHintText || copyHint.Parent is null)
            throw new InvalidOperationException("發票號碼單擊複製提示未建立");
        var columnWidth = Records.Columns.Cast<ColumnHeader>().Sum(column => column.Width);
        if (columnWidth > recordsHost.ColumnViewportWidth ||
            recordsHost.ColumnViewportWidth - columnWidth > 3 ||
            recordsHost.HorizontalScrollVisible || Records.GridLines)
            throw new InvalidOperationException($"已開立發票清單欄寬、水平 scrollbar 或格線繪製方式不正確：欄位 {columnWidth}px，可視 {recordsHost.ColumnViewportWidth}px");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            copyFeedbackTimer.Dispose();
            copyHint.Font.Dispose();
            voidedFont.Dispose();
        }
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
