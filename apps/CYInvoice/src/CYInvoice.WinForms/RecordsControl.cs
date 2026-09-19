using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordsControl : UserControl
{
    private const string CopyHintText = "單擊發票號碼即可複製";
    private static readonly object PlaceholderRow = new();
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly InvoiceSyncCoordinator syncCoordinator;
    private readonly InvoiceDetailRefreshService detailRefreshService;
    private readonly CancellationToken shutdownToken;
    private readonly DateTimePicker dateFrom = DatePicker();
    private readonly DateTimePicker dateTo = DatePicker();
    private readonly TextBox invoiceNumber = UiControls.TextBox(20);
    private readonly TextBox orderId = UiControls.TextBox(40);
    private readonly TextBox buyerName = UiControls.TextBox(200);
    private readonly TextBox buyerBan = UiControls.TextBox(10);
    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox state = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NativeListViewHost recordsHost = new(10F, 22);
    private readonly Button refreshButton = UiControls.StandardButton("重新整理");
    private readonly Button uploadIssuesButton = UiControls.StandardButton("上傳問題");
    private readonly Label rangeToLabel = FilterLabel("至");
    private readonly Label buyerBanLabel = FilterLabel("統編");
    private readonly Label copyHint = new()
    {
        Text = CopyHintText,
        AutoSize = true,
        ForeColor = Color.DimGray,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(12, 10, 0, 0),
    };
    private readonly System.Windows.Forms.Timer copyFeedbackTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer refreshCooldownTimer = new() { Interval = 500 };
    private readonly List<InvoiceRecord> visible = [];
    private readonly Dictionary<string, string> sellerCompanyNames = new(StringComparer.Ordinal);
    private readonly Font sourceTagFont;
    private RecordSortMode sortMode = RecordSortMode.TimeDescending;
    private bool fillingRows;
    private bool selectionClearQueued;
    private bool openingRecord;

    private ListView Records => recordsHost.List;

    public RecordsControl(LocalRepository repository, InvoiceService service)
        : this(repository, service, new InvoiceSyncCoordinator(new InvoiceSyncService(repository)), CancellationToken.None)
    {
    }

    public RecordsControl(
        LocalRepository repository,
        InvoiceService service,
        InvoiceSyncCoordinator syncCoordinator,
        CancellationToken shutdownToken = default)
    {
        this.repository = repository;
        this.service = service;
        this.syncCoordinator = syncCoordinator ?? throw new ArgumentNullException(nameof(syncCoordinator));
        detailRefreshService = new InvoiceDetailRefreshService(repository);
        this.shutdownToken = shutdownToken;
        sourceTagFont = new Font(Records.Font.FontFamily, 7.5F, FontStyle.Bold);
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(18);
        Font = new Font("Microsoft JhengHei UI", 12F);
        copyHint.Font = new Font(Font.FontFamily, 10F);
        copyFeedbackTimer.Tick += (_, _) => ResetCopyHint();
        refreshCooldownTimer.Tick += (_, _) => UpdateRefreshCooldownUi();
        BuildLayout();
        ResetFilters();
        ResetSort();
        Reload();
    }

    private void BuildLayout()
    {
        source.Items.AddRange(["全部", "光貿同步", InvoiceSources.Manual, InvoiceSources.Mo, InvoiceSources.Coupang, InvoiceSources.Digiwin]);
        state.Items.AddRange(["全部", InvoiceStates.Opened, InvoiceStates.OpenedWaitingVoid, InvoiceStates.Failed, InvoiceStates.Unknown, InvoiceStates.Changing, InvoiceStates.Voided]);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 3, Padding = new Padding(0, 4, 0, 6) };
        foreach (var width in new[] { 94, 0, 58, 0, 84, 0, 94, 0 })
            filters.ColumnStyles.Add(width == 0 ? new ColumnStyle(SizeType.Percent, 25) : new ColumnStyle(SizeType.Absolute, width));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        filters.Controls.Add(FilterLabel("開立日期"), 0, 0);
        ConfigureFilterField(dateFrom);
        ConfigureFilterField(dateTo);
        filters.Controls.Add(dateFrom, 1, 0);
        filters.Controls.Add(rangeToLabel, 2, 0);
        filters.Controls.Add(dateTo, 3, 0);
        AddFilter(filters, "發票號碼", invoiceNumber, 4, 0);
        AddFilter(filters, "訂單編號", orderId, 6, 0);
        AddFilter(filters, "買受人", buyerName, 0, 1);
        ConfigureFilterField(buyerBan);
        filters.Controls.Add(buyerBanLabel, 2, 1);
        filters.Controls.Add(buyerBan, 3, 1);
        AddFilter(filters, "來源", source, 4, 1);
        AddFilter(filters, "發票狀態", state, 6, 1);

        var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var query = UiControls.StandardButton("查詢");
        query.Click += (_, _) => { ResetSort(); Reload(); };
        var clear = UiControls.StandardButton("清除條件");
        clear.Click += (_, _) => { ResetFilters(); ResetSort(); Reload(); };
        refreshButton.Click += async (_, _) => await RefreshFromApiAsync();
        uploadIssuesButton.Click += (_, _) =>
        {
            using var form = new SyncIssuesForm(repository);
            form.ShowDialog(FindForm());
            Reload();
        };
        leftButtons.Controls.Add(query);
        leftButtons.Controls.Add(clear);
        leftButtons.Controls.Add(refreshButton);
        leftButtons.Controls.Add(copyHint);
        uploadIssuesButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        uploadIssuesButton.Margin = new Padding(6, 2, 6, 2);
        buttonRow.Controls.Add(leftButtons, 0, 0);
        buttonRow.Controls.Add(uploadIssuesButton, 1, 0);
        filters.Controls.Add(buttonRow, 0, 2);
        filters.SetColumnSpan(buttonRow, 8);

        ConfigureList();
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(recordsHost, 0, 1);
        Controls.Add(root);
    }

    private static void AddFilter(TableLayoutPanel panel, string label, Control field, int column, int row)
    {
        ConfigureFilterField(field);
        panel.Controls.Add(FilterLabel(label), column, row);
        panel.Controls.Add(field, column + 1, row);
    }

    private static Label FilterLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 0, 3, 0),
    };

    private static void ConfigureFilterField(Control field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
    }

    private void ConfigureList()
    {
        Records.Columns.Add("開立時間", 140, HorizontalAlignment.Left);
        Records.Columns.Add("發票號碼", 108, HorizontalAlignment.Left);
        Records.Columns.Add("來源", 122, HorizontalAlignment.Left);
        Records.Columns.Add("訂單編號", 150, HorizontalAlignment.Left);
        Records.Columns.Add("統編", 82, HorizontalAlignment.Left);
        Records.Columns.Add("買受人", 150, HorizontalAlignment.Left);
        Records.Columns.Add("金額", 86, HorizontalAlignment.Right);
        Records.Columns.Add("交付方式", 76, HorizontalAlignment.Left);
        Records.Columns.Add("發票狀態", 136, HorizontalAlignment.Left);
        Records.Columns.Add("上傳", 48, HorizontalAlignment.Center);
        Records.OwnerDraw = true;
        Records.DrawColumnHeader += (_, eventArgs) => NativeListViewHost.DrawHeader(eventArgs, Records.Font);
        Records.DrawItem += (_, eventArgs) => { if (Records.View != View.Details) eventArgs.DrawDefault = true; };
        Records.DrawSubItem += DrawRecordSubItem;
        Records.ColumnClick += (_, eventArgs) => ChangeSort(eventArgs.Column);
        Records.MouseDown += (_, eventArgs) =>
        {
            var hit = Records.HitTest(eventArgs.Location);
            if (hit.Item is null || ReferenceEquals(hit.Item.Tag, PlaceholderRow)) QueueClearSelection();
        };
        Records.MouseClick += CopyInvoiceNumberIfRequested;
        Records.MouseDoubleClick += async (_, eventArgs) =>
        {
            var hit = Records.HitTest(eventArgs.Location);
            if (hit.Item?.Tag is InvoiceRecord record) await OpenSelectedAsync(record);
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
            SortVisible();
            Records.BeginUpdate();
            updateStarted = true;
            Records.Items.Clear();
            foreach (var record in visible)
            {
                var row = NewRow([IssueTime(record), record.InvoiceNumber, InvoiceSourceInference.Display(record), record.OrderId, DisplayBuyerBan(record.BuyerIdentifier),
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
            UpdateUploadIssuesButton();
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
        if (source.SelectedIndex > 0)
        {
            if (source.Text == "光貿同步")
            {
                if (!string.Equals(record.RecordOrigin, RecordOrigins.Sync, StringComparison.Ordinal)) return false;
            }
            else if (!string.Equals(InvoiceSourceInference.Display(record), source.Text, StringComparison.Ordinal)) return false;
        }
        return state.SelectedIndex <= 0 || record.InvoiceState == state.Text;
    }

    private async Task RefreshFromApiAsync()
    {
        refreshCooldownTimer.Stop();
        refreshButton.Enabled = false;
        refreshButton.Text = "重新整理中…";
        try
        {
            var run = await syncCoordinator.RunManualAsync(shutdownToken);
            switch (run.Status)
            {
                case InvoiceSyncRunStatus.Busy:
                    MessageBox.Show(this, "目前正在同步，這次不會重複排程。", "同步進行中",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case InvoiceSyncRunStatus.Cooldown:
                    StartRefreshCooldown();
                    break;
                case InvoiceSyncRunStatus.Completed:
                    ResetSort();
                    Reload();
                    var result = run.Result;
                    if (result is not null && result.Problems.Count != 0)
                    {
                        MessageBox.Show(
                            this,
                            $"同步已完成，但有 {result.Problems.Count} 項未完整更新：\n" + string.Join("\n", result.Problems),
                            "部分資料未能更新",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    StartRefreshCooldown();
                    break;
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "重新整理失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (syncCoordinator.ManualCooldownRemaining <= TimeSpan.Zero)
            {
                refreshCooldownTimer.Stop();
                refreshButton.Text = "重新整理";
                refreshButton.Enabled = true;
            }
            else
            {
                StartRefreshCooldown();
            }
        }
    }

    private void StartRefreshCooldown()
    {
        refreshCooldownTimer.Start();
        UpdateRefreshCooldownUi();
    }

    private void UpdateRefreshCooldownUi()
    {
        var remaining = syncCoordinator.ManualCooldownRemaining;
        if (remaining <= TimeSpan.Zero)
        {
            refreshCooldownTimer.Stop();
            refreshButton.Text = "重新整理";
            refreshButton.Enabled = true;
            return;
        }

        refreshButton.Enabled = false;
        refreshButton.Text = $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} 秒";
    }

    private void UpdateUploadIssuesButton()
    {
        try
        {
            var accountKey = CurrentAccountKey();
            if (accountKey.Length == 0)
            {
                uploadIssuesButton.Text = "上傳問題";
                return;
            }
            var issues = new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved(accountKey);
            var lastRead = new InvoiceSyncStateStore(repository.DataDirectory)
                .LastSuccess(accountKey, SyncIssuesForm.ReadStateScope);
            var unread = issues.Count(issue => lastRead is null || issue.CreatedUtc > lastRead.Value);
            uploadIssuesButton.Text = unread == 0 ? "上傳問題" : $"上傳問題 ({unread})";
        }
        catch (Exception)
        {
            uploadIssuesButton.Text = "上傳問題";
        }
    }

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
        return sellerInvoice.Length == 0 ? string.Empty : settings.Environment + "|" + sellerInvoice;
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

    private async Task OpenSelectedAsync(InvoiceRecord record)
    {
        if (openingRecord) return;
        openingRecord = true;
        Records.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var fresh = await detailRefreshService.RefreshAsync(record, shutdownToken);
            if (shutdownToken.IsCancellationRequested || IsDisposed) return;
            var paper = string.Equals(fresh.Delivery, InvoiceService.DeliveryPaper, StringComparison.Ordinal);
            var sellerCompanyName = paper ? string.Empty : await ResolveSellerCompanyNameAsync();
            Reload();
            using var detail = new RecordDetailForm(fresh, repository, service, sellerCompanyName);
            detail.MinimumSize = new Size(760, 620);
            detail.ClientSize = new Size(800, 700);
            detail.ShowDialog(FindForm());
            if (!IsDisposed) Reload();
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                "目前無法向光貿確認這筆發票的最新資料，因此未開啟詳細資訊。\n\n" + error.Message,
                "無法確認最新資料",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            if (!IsDisposed) Records.Enabled = true;
            openingRecord = false;
        }
    }

    private async Task<string> ResolveSellerCompanyNameAsync()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (settings.Environment == Environments.Test) return "光貿測試公司";

        var accountKey = CurrentAccountKey();
        if (accountKey.Length != 0 && sellerCompanyNames.TryGetValue(accountKey, out var cached))
            return cached;

        try
        {
            var name = (await service.HealthCheckAsync(shutdownToken)).Trim();
            if (name.Length != 0 && accountKey.Length != 0) sellerCompanyNames[accountKey] = name;
            return name;
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return string.Empty;
        }
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
            for (var index = 0; index < 8; index++)
            {
                row.SubItems[index].ForeColor = Color.Gray;
                row.SubItems[index].Font = Records.Font;
            }
            row.SubItems[8].ForeColor = SystemColors.ControlText;
            row.SubItems[8].Font = Records.Font;
            row.SubItems[8].Text = "已作廢";
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
        var widths = new[] { 140, 108, 122, 150, 82, 0, 86, 76, 136, 48 };
        widths[5] = Math.Max(80, available - widths.Sum());
        var over = widths.Sum() - available;
        if (over > 0)
        {
            foreach (var index in new[] { 5, 6, 3, 0, 1 })
            {
                var minimum = index switch { 5 => 60, 3 => 142, 0 => 112, 1 => 90, _ => 70 };
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

        if (eventArgs.Item.Tag is InvoiceRecord record && eventArgs.ColumnIndex == 2)
            DrawSourceSubItem(eventArgs, record);
        else if (eventArgs.Item.Tag is InvoiceRecord stateRecord &&
                 eventArgs.ColumnIndex == 8 &&
                 stateRecord.InvoiceState == InvoiceStates.Voided)
            DrawVoidedStatusSubItem(eventArgs);
        else
            DrawRegularSubItem(eventArgs);

        using (var gridPen = new Pen(Color.FromArgb(190, 190, 190)))
        {
            eventArgs.Graphics.DrawLine(gridPen, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Top, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Bottom);
            eventArgs.Graphics.DrawLine(gridPen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
        }

        if (eventArgs.Item.Tag is InvoiceRecord voidedRecord &&
            voidedRecord.InvoiceState == InvoiceStates.Voided &&
            eventArgs.ColumnIndex < 8)
        {
            using var strikePen = new Pen(Color.FromArgb(125, 125, 125), 1.2F);
            var strikeY = eventArgs.Bounds.Top + eventArgs.Bounds.Height / 2;
            eventArgs.Graphics.DrawLine(strikePen, eventArgs.Bounds.Left, strikeY, eventArgs.Bounds.Right, strikeY);
        }
    }

    private void DrawRegularSubItem(DrawListViewSubItemEventArgs eventArgs)
    {
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= eventArgs.ColumnIndex == 6
            ? TextFormatFlags.Right
            : eventArgs.ColumnIndex == 9 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
        var textBounds = Rectangle.Inflate(eventArgs.Bounds, -5, 0);
        TextRenderer.DrawText(eventArgs.Graphics, eventArgs.SubItem!.Text, eventArgs.SubItem.Font ?? Records.Font,
            textBounds, eventArgs.SubItem.ForeColor, flags);
    }

    private void DrawVoidedStatusSubItem(DrawListViewSubItemEventArgs eventArgs)
    {
        var bounds = Rectangle.Inflate(eventArgs.Bounds, -5, 0);
        var iconSize = Math.Max(12, Math.Min(15, bounds.Height - 6));
        var iconRect = new Rectangle(bounds.Left, bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        using (var brush = new SolidBrush(Color.Firebrick))
            eventArgs.Graphics.FillEllipse(brush, iconRect);
        using (var pen = new Pen(Color.White, 1.6F))
        {
            var inset = Math.Max(3, iconSize / 4);
            eventArgs.Graphics.DrawLine(pen, iconRect.Left + inset, iconRect.Top + inset, iconRect.Right - inset, iconRect.Bottom - inset);
            eventArgs.Graphics.DrawLine(pen, iconRect.Right - inset, iconRect.Top + inset, iconRect.Left + inset, iconRect.Bottom - inset);
        }
        var textBounds = new Rectangle(iconRect.Right + 5, bounds.Top, Math.Max(1, bounds.Right - iconRect.Right - 5), bounds.Height);
        TextRenderer.DrawText(eventArgs.Graphics, "已作廢", Records.Font, textBounds, SystemColors.ControlText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
    }

    private void DrawSourceSubItem(DrawListViewSubItemEventArgs eventArgs, InvoiceRecord record)
    {
        var bounds = Rectangle.Inflate(eventArgs.Bounds, -5, 0);
        var text = eventArgs.SubItem!.Text;
        var tag = InvoiceSourceInference.DisplayTag(record);
        var textFont = eventArgs.SubItem.Font ?? Records.Font;
        var textFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
        if (tag.Length == 0)
        {
            TextRenderer.DrawText(eventArgs.Graphics, text, textFont, bounds, eventArgs.SubItem.ForeColor, textFlags);
            return;
        }

        var tagTextSize = TextRenderer.MeasureText(eventArgs.Graphics, tag, sourceTagFont,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var tagWidth = tagTextSize.Width + 12;
        var sourceAvailable = Math.Max(24, bounds.Width - tagWidth - 6);
        var sourceBounds = new Rectangle(bounds.Left, bounds.Top, sourceAvailable, bounds.Height);
        TextRenderer.DrawText(eventArgs.Graphics, text, textFont, sourceBounds, eventArgs.SubItem.ForeColor, textFlags);

        var tagHeight = Math.Min(18, Math.Max(15, bounds.Height - 4));
        var tagRect = new Rectangle(bounds.Right - tagWidth, bounds.Top + (bounds.Height - tagHeight) / 2, tagWidth, tagHeight);
        var isVoided = record.InvoiceState == InvoiceStates.Voided;
        var isUpdate = string.Equals(tag, InvoiceSourceInference.UpdateTag, StringComparison.Ordinal);
        var background = isVoided
            ? Color.FromArgb(238, 238, 238)
            : isUpdate ? Color.FromArgb(255, 246, 207) : Color.FromArgb(231, 240, 255);
        var border = isVoided
            ? Color.FromArgb(170, 170, 170)
            : isUpdate ? Color.FromArgb(239, 184, 42) : Color.FromArgb(116, 155, 231);
        var foreground = isVoided
            ? Color.DimGray
            : isUpdate ? Color.FromArgb(143, 91, 0) : Color.FromArgb(42, 88, 181);

        var oldSmoothing = eventArgs.Graphics.SmoothingMode;
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedRectangle(tagRect, 4))
        {
            using (var brush = new SolidBrush(background)) eventArgs.Graphics.FillPath(brush, path);
            using (var pen = new Pen(border)) eventArgs.Graphics.DrawPath(pen, path);
        }
        eventArgs.Graphics.SmoothingMode = oldSmoothing;
        TextRenderer.DrawText(eventArgs.Graphics, tag, sourceTagFont, tagRect, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ChangeSort(int column)
    {
        switch (column)
        {
            case 0:
                sortMode = sortMode == RecordSortMode.TimeDescending
                    ? RecordSortMode.TimeAscending
                    : RecordSortMode.TimeDescending;
                break;
            case 1:
                sortMode = sortMode == RecordSortMode.InvoiceDescending
                    ? RecordSortMode.InvoiceAscending
                    : RecordSortMode.InvoiceDescending;
                break;
            case 2:
                sortMode = RecordSortMode.SourceGroup;
                break;
            default:
                return;
        }
        UpdateSortHeaders();
        Reload();
    }

    private void ResetSort()
    {
        sortMode = RecordSortMode.TimeDescending;
        UpdateSortHeaders();
    }

    private void UpdateSortHeaders()
    {
        if (Records.Columns.Count < 3) return;
        Records.Columns[0].Text = sortMode == RecordSortMode.TimeAscending ? "開立時間 ▲" : "開立時間";
        Records.Columns[1].Text = sortMode switch
        {
            RecordSortMode.InvoiceDescending => "發票號碼 ▼",
            RecordSortMode.InvoiceAscending => "發票號碼 ▲",
            _ => "發票號碼",
        };
        Records.Columns[2].Text = sortMode == RecordSortMode.SourceGroup ? "來源 [分組]" : "來源";
    }

    private void SortVisible()
    {
        visible.Sort((left, right) => sortMode switch
        {
            RecordSortMode.TimeAscending => CompareIssueTime(left, right),
            RecordSortMode.InvoiceDescending => CompareInvoiceNumber(left, right, descending: true),
            RecordSortMode.InvoiceAscending => CompareInvoiceNumber(left, right, descending: false),
            RecordSortMode.SourceGroup => CompareSourceGroup(left, right),
            _ => CompareIssueTime(right, left),
        });
    }

    private static int CompareIssueTime(InvoiceRecord left, InvoiceRecord right)
    {
        var comparison = IssueSortTime(left).CompareTo(IssueSortTime(right));
        if (comparison != 0) return comparison;
        return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }

    private static int CompareInvoiceNumber(InvoiceRecord left, InvoiceRecord right, bool descending)
    {
        var leftNumber = left.InvoiceNumber.Trim();
        var rightNumber = right.InvoiceNumber.Trim();
        if (leftNumber.Length == 0 && rightNumber.Length == 0) return CompareIssueTime(right, left);
        if (leftNumber.Length == 0) return 1;
        if (rightNumber.Length == 0) return -1;
        var comparison = string.Compare(leftNumber, rightNumber, StringComparison.OrdinalIgnoreCase);
        if (comparison != 0) return descending ? -comparison : comparison;
        return CompareIssueTime(right, left);
    }

    private static int CompareSourceGroup(InvoiceRecord left, InvoiceRecord right)
    {
        var comparison = SourceRank(InvoiceSourceInference.Display(left)).CompareTo(SourceRank(InvoiceSourceInference.Display(right)));
        if (comparison != 0) return comparison;
        comparison = string.Compare(InvoiceSourceInference.Display(left), InvoiceSourceInference.Display(right), StringComparison.CurrentCultureIgnoreCase);
        if (comparison != 0) return comparison;
        return CompareIssueTime(right, left);
    }

    private static int SourceRank(string value) => value switch
    {
        InvoiceSources.Manual => 0,
        InvoiceSources.Mo => 1,
        InvoiceSources.Coupang => 2,
        InvoiceSources.Digiwin => 3,
        _ => 4,
    };

    private static DateTime IssueSortTime(InvoiceRecord record)
    {
        var combined = (record.InvoiceDate.Trim() + " " + record.InvoiceTime.Trim()).Trim();
        if (DateTime.TryParse(combined, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var issued)) return issued;
        if (DateTime.TryParse(record.SentAt.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var sent)) return sent;
        return DateTime.MinValue;
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
        if (!recordsHost.HeaderClicksEnabled || !recordsHost.UserColumnResizeLocked)
            throw new InvalidOperationException("已開立發票表頭必須可點擊排序且禁止使用者拖拉欄寬");
        if (Math.Abs(Records.Font.SizeInPoints - 10F) > 0.1F) throw new InvalidOperationException("已開立發票清單未使用 10pt 字級");
        if (Records.Items.Count == 0 || Records.GetItemRect(0).Height > 24)
            throw new InvalidOperationException($"已開立發票清單資料列過高：{(Records.Items.Count == 0 ? 0 : Records.GetItemRect(0).Height)}px");
        if (!UiControls.HasLogicalSize(refreshButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            refreshButton.Text != "重新整理")
            throw new InvalidOperationException("已開立發票重新整理按鈕尺寸或文字不正確");
        if (!UiControls.HasLogicalSize(uploadIssuesButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !uploadIssuesButton.Text.StartsWith("上傳問題", StringComparison.Ordinal))
            throw new InvalidOperationException("上傳問題按鈕尺寸或文字不正確");
        if (copyHint.Text != CopyHintText || copyHint.Parent is null)
            throw new InvalidOperationException("發票號碼單擊複製提示未建立");
        if (Records.Columns[0].Text != "開立時間" || Records.Columns[1].Text != "發票號碼" || Records.Columns[2].Text != "來源")
            throw new InvalidOperationException("已開立發票預設排序不應顯示表頭箭頭");

        PerformLayout();
        var firstRowCenters = new Control[] { dateFrom, dateTo, invoiceNumber, orderId }.Select(ScreenCenterY).ToArray();
        var secondRowCenters = new Control[] { buyerName, buyerBan, source, state }.Select(ScreenCenterY).ToArray();
        if (firstRowCenters.Max() - firstRowCenters.Min() > 2 || secondRowCenters.Max() - secondRowCenters.Min() > 2)
            throw new InvalidOperationException("已開立發票篩選欄位未在各列垂直置中對齊");
        if (Math.Abs(rangeToLabel.PointToScreen(Point.Empty).X - buyerBanLabel.PointToScreen(Point.Empty).X) > 1)
            throw new InvalidOperationException("統編標籤左緣未與日期『至』左緣對齊");
        if (Math.Abs(ScreenCenterY(refreshButton) - ScreenCenterY(uploadIssuesButton)) > 1)
            throw new InvalidOperationException("上傳問題按鈕未與左側操作按鈕垂直對齊");
        if (Records.Columns[2].Width < 122 || Records.Columns[3].Width < 142 || Records.Columns[8].Width < 128)
            throw new InvalidOperationException("已開立發票來源、訂單編號或發票狀態欄位過窄");

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
            refreshCooldownTimer.Dispose();
            copyHint.Font.Dispose();
            sourceTagFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private static ListViewItem NewRow(IReadOnlyList<string> values)
    {
        var row = new ListViewItem(values[0]);
        for (var index = 1; index < values.Count; index++) row.SubItems.Add(values[index]);
        return row;
    }

    private static int ScreenCenterY(Control control) => control.PointToScreen(Point.Empty).Y + (control.Height / 2);
    private static DateTimePicker DatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy/MM/dd",
        ShowCheckBox = true,
    };
    private static bool Contains(string value, string search) => search.Trim().Length == 0 || value.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private static string DisplayBuyerBan(string value) => value.Trim() == "0000000000" ? string.Empty : value.Trim();
    private static DateTime? ParseDate(string value)
    {
        value = value.Trim();
        if (value.Length >= 10) value = value[..10];
        return DateTime.TryParseExact(value, ["yyyy/MM/dd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static string IssueTime(InvoiceRecord record)
    {
        if (record.InvoiceDate.Trim().Length != 0)
        {
            var time = record.InvoiceTime.Trim();
            if (time.Length >= 5) time = time[..5];
            return (record.InvoiceDate.Trim() + " " + time).Trim();
        }

        var sentAt = record.SentAt.Trim();
        if (DateTime.TryParse(sentAt, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        return sentAt.Length >= 16 ? sentAt[..16] : sentAt;
    }

    private enum RecordSortMode
    {
        TimeDescending,
        TimeAscending,
        InvoiceDescending,
        InvoiceAscending,
        SourceGroup,
    }
}
