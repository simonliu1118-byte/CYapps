using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SyncIssuesForm : Form
{
    internal const string ReadStateScope = "upload-issues-read";
    private static readonly int[] DefaultWidths = [145, 130, 110, 150, 0, 90];

    private readonly bool processing;
    private readonly LocalRepository repository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly FailedInvoiceRecordStore failedStore;
    private readonly EmployeeVoidWorkflowService voidWorkflow;
    private readonly EmployeeAllowanceWorkflowService allowanceWorkflow;
    private readonly EmployeeAllowanceVoidWorkflowService allowanceVoidWorkflow;
    private readonly InvoiceAdministrativeClosureService administrativeClosure;
    private readonly string accountKey;
    private DateTimeOffset? lastRead;
    private bool manualReviewBusy;
    private bool settingColumnWidths;
    private readonly ToolTip contentToolTip = new();
    private string visibleToolTip = string.Empty;

    private readonly bool startupSmokeTest;
    private const int MaximumVisibleRows = 12;
    private readonly NativeListViewHost listHost = new(fontSize: 10F, rowHeight: 22);
    private ListView list => listHost.List;
    private readonly Panel body = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label empty = new()
    {
        Text = "無資料", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.DimGray, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty,
    };
    private TableLayoutPanel root = null!;
    private readonly Label summary = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Anchor = AnchorStyles.Left,
    };
    private readonly Button deleteFailed = UiControls.StandardButton("刪除勾選");
    private readonly Button clearFailed = UiControls.StandardButton("清除開立失敗紀錄");
    private readonly Button reportCrossDevice = UiControls.StandardButton("回報跨機");
    private readonly Button exportIssueDiagnostics = UiControls.StandardButton("匯出問題診斷");

    public SyncIssuesForm(LocalRepository repository, bool processing = false, bool startupSmokeTest = false)
    {
        this.processing = processing;
        this.startupSmokeTest = startupSmokeTest;
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        failedStore = new FailedInvoiceRecordStore(repository.DataDirectory);
        voidWorkflow = new EmployeeVoidWorkflowService(repository);
        allowanceWorkflow = new EmployeeAllowanceWorkflowService(repository);
        allowanceVoidWorkflow = new EmployeeAllowanceVoidWorkflowService(repository);
        administrativeClosure = new InvoiceAdministrativeClosureService(repository);
        accountKey = CurrentAccountKey();
        lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);

        Text = processing ? "處理中" : "上傳問題";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 0);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(960, 520);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.White;
        ShowIcon = false;

        BuildLayout();
        if (!processing) MarkVisibleIssuesRead();
        ReloadAll();
        Shown += (_, _) => FitToRows();
    }

    private void BuildLayout()
    {
        list.CheckBoxes = !processing;
        list.MultiSelect = true;
        list.HideSelection = false;
        list.GridLines = true;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        ConfigureList();
        var reload = UiControls.StandardButton("重新整理");
        reload.Click += (_, _) => ReloadAll();
        reportCrossDevice.Click += async (_, _) => await ReportCrossDeviceAsync();
        list.DoubleClick += async (_, _) => await ShowSelectedIssueDetailsAsync();
        list.ItemCheck += (_, eventArgs) =>
        {
            if (eventArgs.Index < 0 || eventArgs.Index >= list.Items.Count) return;
            if (eventArgs.NewValue == CheckState.Checked && (processing || list.Items[eventArgs.Index].Tag is not InvoiceRecord))
                eventArgs.NewValue = CheckState.Unchecked;
        };
        list.ItemChecked += (_, _) => BeginInvoke((Action)UpdateFailedButtons);
        list.SelectedIndexChanged += (_, _) => { UpdateCrossDeviceButton(); UpdateDiagnosticButton(); };
        list.ClientSizeChanged += (_, _) => LayoutColumns();
        list.ColumnWidthChanging += (_, eventArgs) =>
        {
            if (settingColumnWidths) return;
            eventArgs.Cancel = true;
            eventArgs.NewWidth = list.Columns[eventArgs.ColumnIndex].Width;
        };
        list.MouseMove += ShowClippedContentToolTip;
        list.MouseLeave += (_, _) => ClearContentToolTip();

        deleteFailed.Enabled = false;
        deleteFailed.Click += (_, _) => DeleteCheckedFailed();
        clearFailed.Enabled = false;
        clearFailed.Click += (_, _) => ClearAllFailed();
        exportIssueDiagnostics.Click += (_, _) => ExportSelectedIssueDiagnostics();

        var close = UiControls.StandardButton("關閉");
        close.Click += (_, _) => Close();

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(summary, 0, 0);
        reportCrossDevice.Margin = new Padding(0, 0, 6, 0);
        header.Controls.Add(reportCrossDevice, 1, 0);
        exportIssueDiagnostics.Margin = new Padding(0, 0, 6, 0);
        header.Controls.Add(exportIssueDiagnostics, 2, 0);
        reload.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        header.Controls.Add(reload, 3, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var failedActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        if (!processing)
        {
            failedActions.Controls.Add(deleteFailed);
            failedActions.Controls.Add(clearFailed);
        }
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Margin = new Padding(0, 4, 0, 0);
        bottom.Controls.Add(failedActions, 0, 0);
        bottom.Controls.Add(close, 1, 0);

        root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            Tag = "upload-issues-root",
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(header, 0, 0);
        body.Controls.Add(listHost);
        body.Controls.Add(empty);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
        Resize += (_, _) => LayoutColumns();
    }

    private void ConfigureList()
    {
        list.Columns.Add("日期/時間", DefaultWidths[0], HorizontalAlignment.Left);
        list.Columns.Add("類型", DefaultWidths[1], HorizontalAlignment.Left);
        list.Columns.Add("發票號碼", DefaultWidths[2], HorizontalAlignment.Left);
        list.Columns.Add("訂單編號", DefaultWidths[3], HorizontalAlignment.Left);
        list.Columns.Add("內容", DefaultWidths[4], HorizontalAlignment.Left);
        list.Columns.Add("狀態", DefaultWidths[5], HorizontalAlignment.Center);
    }

    private void ReloadAll()
    {
        try
        {
            lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);
            var queue = new InvoiceWorkQueue(repository).Load();
            var issues = processing ? queue.ProcessingIssues : queue.UploadIssues;
            var failed = processing ? Array.Empty<InvoiceRecord>() : CurrentFailedRecords();
            var rows = new List<(string SortKey, ListViewItem Row)>();

            foreach (var issue in issues)
            {
                var state = IssueState(issue);
                var time = issue.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                var row = new ListViewItem(ShortIssueTime(time));
                row.SubItems.Add(processing && IsAllowanceManualReview(issue) ? "折讓處理中" :
                    processing && IsAllowanceVoidManualReview(issue) ? "折讓作廢待主管" :
                    processing && IsVoidManualReview(issue) ? "紙本作廢待主管" : DisplayType(issue.IssueType));
                row.SubItems.Add(issue.InvoiceNumber);
                row.SubItems.Add(issue.OrderId);
                row.SubItems.Add(issue.Message);
                row.SubItems.Add(state);
                row.Tag = issue;
                var stateCell = row.SubItems[5];
                stateCell.ForeColor = state switch
                {
                    "未讀" => Color.Firebrick,
                    "人工確認" or "等待確認" or "可結案" => Color.FromArgb(190, 120, 0),
                    "已解決" or "已人工處理" => Color.FromArgb(0, 132, 72),
                    _ => Color.DimGray,
                };
                if (state is "人工確認" or "等待確認" or "可結案")
                    stateCell.Font = new Font(list.Font, FontStyle.Bold);
                rows.Add((time, row));
            }

            if (processing)
            {
                foreach (var item in queue.ProcessingItems)
                {
                    var time = FullIssueTime(item.Record);
                    var row = new ListViewItem(ShortIssueTime(time));
                    row.SubItems.Add(item.Kind == InvoiceProcessingKind.Void ? "作廢處理中" : "發票上傳處理中");
                    row.SubItems.Add(item.Record.InvoiceNumber);
                    row.SubItems.Add(item.Record.OrderId);
                    row.SubItems.Add(item.Message);
                    row.SubItems.Add(item.Kind == InvoiceProcessingKind.Void && administrativeClosure.CanClose(item.Record) ? "可結案" : "處理中");
                    row.Tag = item;
                    row.SubItems[5].ForeColor = Color.FromArgb(190, 120, 0);
                    rows.Add((time, row));
                }
            }

            foreach (var record in failed)
            {
                var time = FullIssueTime(record);
                var content = FriendlyFailureReason(record.ErrorMessage);
                if (record.BuyerName.Trim().Length != 0) content = record.BuyerName.Trim() + "｜" + content;
                var row = new ListViewItem(ShortIssueTime(time));
                row.SubItems.Add("開立失敗");
                row.SubItems.Add(record.InvoiceNumber);
                row.SubItems.Add(record.OrderId);
                row.SubItems.Add(content);
                row.SubItems.Add("開立失敗");
                row.Tag = record;
                row.SubItems[5].ForeColor = Color.Firebrick;
                rows.Add((time, row));
            }

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                var index = 0;
                foreach (var row in rows.OrderByDescending(item => item.SortKey, StringComparer.Ordinal))
                {
                    ApplyZebra(row.Row, index++);
                    list.Items.Add(row.Row);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            var unresolved = issues.Count(issue => issue.ResolvedUtc is null);
            summary.Text = processing ? $"處理中：{queue.ProcessingCount} 項" : $"尚未解決：{unresolved} 項｜開立失敗：{failed.Count} 筆";
            UpdateFailedButtons();
            UpdateCrossDeviceButton();
            UpdateDiagnosticButton();
            FitToRows();
            LayoutColumns();
        }
        catch (Exception error)
        {
            if (startupSmokeTest) throw;
            MessageBox.Show(this, "讀取" + Text + "失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FitToRows()
    {
        var hasRows = list.Items.Count > 0;
        empty.Visible = !hasRows;
        listHost.Visible = hasRows;
        var chrome = root.Padding.Vertical + (int)Math.Ceiling(root.RowStyles[0].Height + root.RowStyles[2].Height);
        var workArea = Screen.FromControl(this).WorkingArea;
        var maximumBodyHeight = Math.Max(listHost.HeightForRows(1), workArea.Height - (Height - ClientSize.Height) - chrome - 40);
        var visibleRows = Math.Min(MaximumVisibleRows, list.Items.Count);
        while (visibleRows > 1 && listHost.HeightForRows(visibleRows) > maximumBodyHeight) visibleRows--;
        var bodyHeight = hasRows ? listHost.HeightForRows(visibleRows) : Math.Max(40, empty.Font.Height + 16);
        ClientSize = new Size(ClientSize.Width, chrome + bodyHeight);
        root.PerformLayout();
        if (Visible && Bottom > workArea.Bottom) Top = Math.Max(workArea.Top, workArea.Bottom - Height);
    }

    internal void VerifySmokeRowSizing()
    {
        var original = list.Items.Cast<ListViewItem>().ToArray();
        try
        {
            foreach (var count in new[] { 0, 1, 4, 20, 1, 0 })
            {
                list.Items.Clear();
                for (var index = 0; index < count; index++)
                    list.Items.Add(new ListViewItem(["2026/10/09", "合成測試", "AA00000000", "synthetic", "測試資料", "處理中"]));
                FitToRows();
                Application.DoEvents();
                if (count == 0)
                {
                    if (!empty.Visible || listHost.Visible || empty.Text != "無資料")
                        throw new InvalidOperationException("空待辦清單必須只顯示無資料");
                }
                else if (listHost.VisibleRowCapacity() != Math.Min(count, MaximumVisibleRows))
                    throw new InvalidOperationException($"Issue viewport: items={count}, capacity={listHost.VisibleRowCapacity()}, height={listHost.Height}; expected actual rows capped at {MaximumVisibleRows}.");
                if (body.Bottom > root.ClientSize.Height || body.Height <= 0)
                    throw new InvalidOperationException("待辦動態高度超出視窗");
            }
        }
        finally
        {
            list.Items.Clear();
            list.Items.AddRange(original);
            FitToRows();
        }
    }

    private void MarkVisibleIssuesRead()
    {
        try
        {
            var openedAt = DateTimeOffset.Now;
            stateStore.SetLastSuccess(accountKey, ReadStateScope, openedAt);
            lastRead = openedAt;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "更新上傳問題已讀狀態失敗：" + error.Message, "狀態更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ShowSelectedIssueDetailsAsync()
    {
        if (manualReviewBusy) return;
        if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is InvoiceProcessingItem item)
        {
            ShowProcessingDetails(item);
            return;
        }
        if (SelectedIssue() is not { } issue) return;
        try
        {
            var manual = IsManualReview(issue);
            var rows = IsAllowanceVoidManualReview(issue)
                ? AllowanceVoidDetailRows(issue)
                : IsAllowanceManualReview(issue)
                    ? AllowanceDetailRows(issue)
                    : IsVoidManualReview(issue)
                        ? VoidDetailRows(issue)
                        : CommonDetailRows(issue).Append(new("處理狀態", issue.ResolvedUtc is null ? IssueState(issue) : "已解決")).ToArray();

            var primaryText = string.Empty;
            var allowCancel = false;
            var allowAdministrativeClose = false;
            var primaryDanger = false;
            var secondaryText = string.Empty;

            if (issue.ResolvedUtc is null)
            {
                if (administrativeClosure.IsActiveWorkIssue(issue))
                {
                    try { allowAdministrativeClose = administrativeClosure.CanClose(issue); }
                    catch { allowAdministrativeClose = false; }
                }
                if (IsAllowanceVoidManualReview(issue) && allowanceVoidWorkflow.LocalApprovalFor(issue)?.Claimed == true)
                    allowAdministrativeClose = false;

                if (!allowAdministrativeClose && IsVoidManualReview(issue))
                {
                    primaryText = "確認送出作廢";
                    primaryDanger = true;
                    allowCancel = true;
                }
                else if (IsAllowanceVoidManualReview(issue) && (!allowAdministrativeClose ||
                    (repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                     repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly)))
                {
                    var local = repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                        repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly;
                    var approval = local ? allowanceVoidWorkflow.LocalApprovalFor(issue) : null;
                    primaryText = !local ? LegacyAllowanceVoidWebsiteAction :
                        approval?.Claimed == true ? "查回作廢狀態" : "核准並作廢";
                    primaryDanger = local && approval?.Claimed != true;
                    allowCancel = !allowAdministrativeClose && approval?.Claimed != true;
                    // The old website workflow remains available until the new
                    // workflow has real acceptance. A claimed POST cannot use it.
                    if (local && !allowAdministrativeClose && approval?.Claimed != true) secondaryText = LegacyAllowanceVoidWebsiteAction;
                }
                else if (!allowAdministrativeClose && IsAllowanceManualReview(issue))
                {
                    var record = FindIssueRecord(issue);
                    var review = allowanceWorkflow.ManualReviewFor(record);
                    if (review is not null)
                    {
                        if (review.LocalApprovedUtc is not null)
                        {
                            var local = repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                                repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly;
                            var reservation = local ? allowanceWorkflow.LocalReservationFor(issue) : null;
                            primaryText = !local ? string.Empty :
                                LocalAllowancePrimaryAction(reservation?.Status);
                            primaryDanger = reservation?.Status == "reserved";
                            allowCancel = false;
                        }
                        else if (!review.AwaitingConfirmation)
                        {
                            var local = repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                                repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly;
                            primaryText = local && review.DraftItems is { Count: > 0 }
                                ? "核准並開立折讓"
                                : LegacyAllowanceWebsiteAction;
                            allowCancel = true;
                        }
                        else
                        {
                            var candidates = NewAllowanceCandidates(record, review);
                            if (candidates.Count > 1 && candidates.All(item => !IsPendingAllowanceStatus(item.InvoiceStatus)))
                                primaryText = "確認折讓單號";
                        }
                    }
                }
                else if (!allowAdministrativeClose && !manual && !administrativeClosure.IsActiveWorkIssue(issue))
                {
                    primaryText = "標記已解決";
                }
            }

            using var detail = new ManualReviewDetailForm(
                DisplayType(issue.IssueType),
                rows,
                primaryText,
                allowCancel,
                allowAdministrativeClose,
                primaryDanger,
                secondaryText);
            if (detail.ShowDialog(this) != DialogResult.OK) return;

            switch (detail.SelectedAction)
            {
                case ManualReviewDetailAction.AdministrativeClose:
                    CloseAdministrativeWork(issue);
                    break;
                case ManualReviewDetailAction.CancelReturn:
                    CancelManualReview(issue);
                    break;
                case ManualReviewDetailAction.Primary:
                    if (manual) await ProcessManualReviewAsync(issue);
                    else ResolveIssue(issue);
                    break;
                case ManualReviewDetailAction.Secondary:
                    if (IsAllowanceVoidManualReview(issue)) CompleteAllowanceVoid(issue);
                    break;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "讀取待辦明細失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowProcessingDetails(InvoiceProcessingItem item)
    {
        try
        {
            var canClose = item.Kind == InvoiceProcessingKind.Void && administrativeClosure.CanClose(item.Record);
            using var detail = new ManualReviewDetailForm("處理中", [
                new("發票號碼", item.Record.InvoiceNumber), new("訂單編號", item.Record.OrderId),
                new("開立時間", FullIssueTime(item.Record)), new("目前訊息", item.Message),
                new("官方上傳狀態", item.Record.UploadStatusText), new("最後回查", item.Record.LastChecked),
            ], string.Empty, false, canClose);
            if (detail.ShowDialog(this) == DialogResult.OK && detail.SelectedAction == ManualReviewDetailAction.AdministrativeClose)
                CloseAdministrativeWork("作廢", (employee, password) => administrativeClosure.Close(item.Record, employee, password));
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "讀取處理中明細失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResolveIssue(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || IsManualReview(issue) || administrativeClosure.IsActiveWorkIssue(issue)) return;
        try
        {
            issueStore.Resolve(issue.Id, DateTimeOffset.Now);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "標記上傳問題失敗：" + error.Message, "操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseAdministrativeWork(InvoiceSyncIssue issue)
    {
        if (manualReviewBusy || !administrativeClosure.CanClose(issue)) return;
        var operation = IsAllowanceManualReview(issue) ? "折讓" : "作廢";
        CloseAdministrativeWork(operation, (employee, password) => administrativeClosure.Close(issue, employee, password));
    }

    private void CloseAdministrativeWork(string operation, Action<string, string> closeWork)
    {
        if (MessageBox.Show(
                this,
                $"這筆{operation}待處理作業已超過系統保留的兩期。\n\n管理員結案後，CYInvoice 會停止追蹤這筆作業、清除本機等待標記，之後可依舊資料保留規則清除。\n\n此操作不會呼叫光貿 API，也不代表光貿已完成{operation}。確定結案？",
                "管理員結案",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository, "管理員結案－驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            closeWork(login.EnteredEmployeeNo, login.AuthenticatedPassword);
            MessageBox.Show(this, "已由管理員手動結案。CYInvoice 不會再自動追蹤這筆待處理作業。", "結案完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "管理員結案失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ProcessManualReviewAsync(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || manualReviewBusy) return;
        if (IsVoidManualReview(issue))
        {
            await ApproveVoidManualReviewAsync(issue);
            return;
        }
        if (IsAllowanceVoidManualReview(issue))
        {
            if (repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly)
                await ProcessLocalAllowanceVoidAsync(issue);
            else CompleteAllowanceVoid(issue);
            return;
        }

        InvoiceRecord record;
        InvoiceAllowanceManualReview review;
        try
        {
            record = FindIssueRecord(issue);
            review = allowanceWorkflow.ManualReviewFor(record)
                ?? throw new InvalidOperationException("這筆折讓人工處理已沒有申請資料");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓人工處理無法繼續", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (review.LocalApprovedUtc is not null)
        {
            var reservation = allowanceWorkflow.LocalReservationFor(issue);
            if (reservation is null)
                await ReserveLocalAllowanceDraftAsync(issue);
            else if (reservation.Status == "reserved")
                await SendLocalAllowanceDraftAsync(issue, reservation);
            else if (reservation.Status == "sending")
                await ReconcileLocalAllowanceDraftAsync(issue);
            else if (reservation.Status == "occupied_external")
                await SendLocalAllowanceDraftAsync(issue, reservation);
            return;
        }
        else if (!review.AwaitingConfirmation)
        {
            if (repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly &&
                review.DraftItems is { Count: > 0 })
                await ApproveLocalAllowanceDraftAsync(issue);
            else await CompleteAllowanceAsync(issue);
        }
        else await ConfirmAllowanceCandidateAsync(issue, record, review);
    }

    private async Task ProcessLocalAllowanceVoidAsync(InvoiceSyncIssue issue)
    {
        using var login = new EmployeeAdminLoginForm(repository,
            "折讓作廢－主管當次驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var prior = allowanceVoidWorkflow.LocalApprovalFor(issue);
            if (prior?.Claimed == true)
            {
                // No second g0501 after a previous durable claim, including
                // a lost response. Read the SAME official number instead.
                var observed = await allowanceVoidWorkflow.ReadbackLocalVoidAsync(issue,
                    login.EnteredEmployeeNo, login.AuthenticatedPassword);
                if (!IsDisposed) MessageBox.Show(this, observed.Message,
                    "原號作廢查回", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var preview = await allowanceVoidWorkflow.PreviewLocalVoidAsync(issue,
                    login.EnteredEmployeeNo, login.AuthenticatedPassword);
                if (IsDisposed) return;
                var message = $"原折讓號：{preview.AllowanceNumber}\n" +
                    $"原發票：{preview.OriginalInvoiceNumber}\n" +
                    $"買受人：{preview.BuyerName}\n" +
                    $"未稅金額：{preview.NetAmount}\n" +
                    $"稅額：{preview.TaxAmount}\n\n" +
                    "確認後程式立即重驗主管與光貿資料，保存核准並送出原號作廢。" +
                    "不必再輸入原號或按第二次送出。結果不明只能查原號，不會盲目重送。" +
                    "\n\n確定核准作廢？";
                if (MessageBox.Show(this, message, "主管核准並作廢",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                var result = await allowanceVoidWorkflow.ApproveAndExecuteLocalVoidAsync(
                    issue, login.EnteredEmployeeNo, login.AuthenticatedPassword,
                    preview.OfficialSnapshotHash);
                if (!IsDisposed) MessageBox.Show(this, result.Message, "折讓作廢查證",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (!IsDisposed) ReloadAll();
        }
        catch (Exception error)
        {
            if (!IsDisposed) MessageBox.Show(this, error.Message +
                "\n\n若核准或送出回覆遺失，先依原號查證；不要重新建立申請。",
                "折讓作廢尚待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) SetManualReviewBusy(false); }
    }

    private void CompleteAllowanceVoid(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(
                this,
                "此為舊網站作廢流程，僅供新流程完成真實驗收前處理既有待辦。\n\n請先在光貿網站完成作廢並另行核對官方狀態。\n\n按下「是」只結束人工待辦，不會呼叫 g0501，也不表示官方已作廢成功。",
                "舊流程：網站作廢人工結案",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "折讓作廢人工處理－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            allowanceVoidWorkflow.MarkManualCompleted(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            MessageBox.Show(this, "僅完成舊人工待辦的行政結案；光貿作廢狀態仍以官方唯讀查回為準。", "舊流程行政結案", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓作廢人工處理未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ApproveVoidManualReviewAsync(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(this,
                "這筆作廢申請的紙本電子發票證明聯先前尚未收回。\n\n確認後，CYInvoice 會重新查詢光貿最新狀態，再送出作廢。請確認已完成主管核准並可進行作廢。",
                "確認送出作廢", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "人工確認－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await voidWorkflow.ApproveManualReviewAsync(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            switch (result.Outcome)
            {
                case InvoiceVoidOutcome.Confirmed:
                case InvoiceVoidOutcome.AlreadyVoided:
                    MessageBox.Show(this, "光貿已確認發票作廢完成。", "作廢完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case InvoiceVoidOutcome.PendingConfirmation:
                    MessageBox.Show(this, "作廢已送出，但光貿尚未確認最終結果。\n\n發票目前為「已開立 (等待作廢)」；後續每次正常同步都會再查詢官方結果。",
                        "作廢結果待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.Rejected:
                    MessageBox.Show(this, result.Message + "\n\n人工確認仍保留，尚未標記完成。", "作廢未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.RetryReady:
                    MessageBox.Show(this, "先前等待作廢狀態已解除，本次沒有自動重送。\n人工確認仍保留，可重新確認後再次送出。", "請重新確認",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "人工確認未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ReserveLocalAllowanceDraftAsync(InvoiceSyncIssue issue)
    {
        if (manualReviewBusy || issue.ResolvedUtc is not null) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "Local 折讓預留單號－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var reservation = await allowanceWorkflow.ReserveLocalApprovedDraftAsync(
                issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            if (IsDisposed) return;
            MessageBox.Show(this,
                $"已安全預留折讓號碼：{reservation.AllowanceNumber}\n" +
                "完整請求內容已保存於本機 SQLite，程式重開後仍使用同一號碼。\n\n" +
                "目前只有預留，沒有向光貿開立折讓。正式送出需由原核准管理員再次登入，" +
                "確認後只會呼叫一次 g0401；結果不明只能依原號查回。",
                "已預留（未送出）", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this, error.Message,
                    "預留折讓單號失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { if (!IsDisposed) SetManualReviewBusy(false); }
    }

    // This is presentation-only: the actual authority and evidence gates
    // remain in EmployeeAllowanceWorkflowService and the existing Local Ledger.
    private static string LocalAllowancePrimaryAction(string? status) => status switch
    {
        null => "預留折讓單號（不送出）",
        "reserved" => "正式送出折讓（單次）",
        "sending" => "依原折讓號碼唯讀查回",
        "occupied_external" => "繼續自動加號並送出",
        _ => string.Empty
    };

    private async Task ReviewLocalAllowanceCollisionAsync(
        InvoiceSyncIssue issue, LocalAllowanceReservation previous)
    {
        if (manualReviewBusy || issue.ResolvedUtc is not null) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "Local 外部占號重新配號－原核准管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        const string words = "確認外部占號並重新配號";
        using var dialog = new Form
        {
            Text = "外部占號－人工覆核",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(650, 256),
            MinimumSize = new Size(590, 246),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ShowIcon = false,
            Font = new Font("Microsoft JhengHei UI", 10F),
            BackColor = Color.White
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 3
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var explanation = new Label
        {
            Dock = DockStyle.Fill, AutoSize = false,
            Text = "原號：" + previous.AllowanceNumber +
                "\n只有第一次 g0401 確認被光貿拒絕占號，且即時查回證明該號屬於" +
                "不同買方與不同原發票、主管核准仍有效，才能預留新號。" +
                "\n本次操作只預留新號，不會送出 g0401；舊號與稽核永久保留。" +
                "\n請在下方完整輸入：「" + words + "」",
            TextAlign = ContentAlignment.MiddleLeft
        };
        var typed = UiControls.TextBox(40);
        typed.Dock = DockStyle.Fill;
        typed.AccessibleName = "人工確認外部占號並重新配號";
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty
        };
        var confirm = UiControls.StandardButton("確認預留");
        confirm.Width = 112;
        confirm.Enabled = false;
        typed.TextChanged += (_, _) =>
            confirm.Enabled = typed.Text == words;
        confirm.DialogResult = DialogResult.OK;
        var cancel = UiControls.StandardButton("取消");
        cancel.Width = 96;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(confirm);
        panel.Controls.Add(explanation, 0, 0);
        panel.Controls.Add(typed, 0, 1);
        panel.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(panel);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            typed.Text != words) return;

        SetManualReviewBusy(true);
        try
        {
            var next = await allowanceWorkflow.ReserveLocalAfterExternalCollisionAsync(
                issue, login.EnteredEmployeeNo, login.AuthenticatedPassword, words);
            if (IsDisposed) return;
            MessageBox.Show(this,
                "前次占號：" + previous.AllowanceNumber + "\n新預留號：" +
                next.AllowanceNumber +
                "\n已寫入原 Local Ledger 與不可修改的主管覆核紀錄。" +
                "\n這不是官方成功紀錄，也沒有重新發送；後續必須由原核准管理員另行確認首次送出。",
                "人工覆核完成（只預留）",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this,
                    error.Message + "\n\n新號未經驗證不可送出；請查看原 Ledger。",
                    "未能安全改配號", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) SetManualReviewBusy(false); }
    }

    private async Task SendLocalAllowanceDraftAsync(
        InvoiceSyncIssue issue, LocalAllowanceReservation reservation)
    {
        if (manualReviewBusy || issue.ResolvedUtc is not null) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "Local 正式折讓送出－原核准管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        var environment = repository.Settings.LoadOrCreate().Environment;
        var prompt = $"環境：{environment}\n折讓單號：{reservation.AllowanceNumber}\n\n" +
            "這會實際呼叫光貿 g0401 開立折讓，並且只允許一次送出。\n" +
            "送出前將重新檢查管理員權限、核准草稿與光貿最新折讓及餘額。\n" +
            "一旦進入 sending，斷線、逾時或關閉程式都不得重新送出或換號。\n\n" +
            "確定要正式送出這筆折讓？";
        if (MessageBox.Show(this, prompt, "正式折讓交易－最後確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.SendLocalReservedDraftAsync(
                issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            if (IsDisposed) return;
            ShowLocalAllowanceSendResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this, error.Message +
                    "\n\n請先重新查看原號狀態，不得重按送出。",
                    "折讓正式送出未確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) SetManualReviewBusy(false); }
    }

    private async Task ReconcileLocalAllowanceDraftAsync(InvoiceSyncIssue issue)
    {
        if (manualReviewBusy || issue.ResolvedUtc is not null) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "Local 折讓原號查回－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.ReconcileLocalReservedDraftAsync(
                issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            if (IsDisposed) return;
            ShowLocalAllowanceSendResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this, error.Message, "原號唯讀查回失敗",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) SetManualReviewBusy(false); }
    }

    private void ShowLocalAllowanceSendResult(LocalAllowanceSendResult result)
    {
        MessageBox.Show(this,
            $"原折讓單號：{result.AllowanceNumber}\n{result.Message}\n\n" +
            "每次配號與送出結果均保留。結果不明時只能查原號，" +
            "不能把官方既有同號直接當成本次新折讓；不得盲目重送。",
            "折讓結果待人工核對", MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private async Task ApproveLocalAllowanceDraftAsync(InvoiceSyncIssue issue)
    {
        if (manualReviewBusy || issue.ResolvedUtc is not null) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "Local 折讓核准－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            // The manager sees a fresh official snapshot before signing off.
            var preview = await allowanceWorkflow.PreviewLocalDraftAsync(issue,
                login.EnteredEmployeeNo, login.AuthenticatedPassword);
            if (IsDisposed) return;
            var message =
                $"原發票：{preview.InvoiceNumber}\n" +
                $"本次折讓含稅金額：{MoneyFormatter.Integer(preview.RequestedGross)}\n" +
                $"光貿當下剩餘可折金額：{preview.OfficialRemainingGross}\n\n" +
                "光貿目前已有折讓（包含已作廢與歷史紀錄）：\n" +
                FormatAllowances(preview.OfficialHistory) +
                "\n\n按下確認後，程式將依本次核准連續查證、配號及送出光貿 g0401。" +
                "只有明確佔號拒絕才自動改配下一號，不設換號次數上限。" +
                "若逾時、回覆不明或其他錯誤，保留原號並停止送出。" +
                "\n\n是否核准並開立？";
            if (MessageBox.Show(this, message, "Local 折讓覆核－光貿即時查回",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            using var stop = new CancellationTokenSource();
            using var progressWindow = new Form
            {
                Text = "折讓開立進度",
                ClientSize = new Size(570, 148),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowIcon = false,
                ShowInTaskbar = false, Font = Font,
                ControlBox = false
            };
            var progressText = new Label
            {
                Dock = DockStyle.Top, Height = 75, Padding = new Padding(12),
                Text = "正在核准並保存原申請；此時取消不能撤回已發出的光貿請求。"
            };
            var stopButton = UiControls.StandardButton("停止後續嘗試");
            stopButton.Dock = DockStyle.Bottom;
            stopButton.Click += (_, _) => { stop.Cancel(); stopButton.Enabled = false; };
            progressWindow.Controls.Add(progressText);
            progressWindow.Controls.Add(stopButton);
            progressWindow.Show(this);
            try
            {
                var result = await allowanceWorkflow.ApproveAndIssueLocalDraftAsync(
                    issue, login.EnteredEmployeeNo, login.AuthenticatedPassword,
                    preview.OfficialSnapshotHash,
                    message =>
                    {
                        if (progressWindow.IsDisposed || !progressWindow.IsHandleCreated) return;
                        progressWindow.BeginInvoke((Action)(() =>
                        {
                            if (!progressWindow.IsDisposed) progressText.Text = message;
                        }));
                    },
                    stop.Token);
                if (!IsDisposed) ShowLocalAllowanceSendResult(result);
            }
            finally { progressWindow.Close(); }
            if (!IsDisposed) ReloadAll();
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this, error.Message,
                    "Local 折讓核准未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { if (!IsDisposed) SetManualReviewBusy(false); }
    }

    private async Task CompleteAllowanceAsync(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(this,
                "此為舊網站折讓流程，僅供新流程真實驗收完成前處理既有待辦。\n\n請先確認光貿網站已進行折讓。按下「是」只會進入官方 invoice_query 唯讀確認，不等於本次已成功開立或可重新送出。",
                "舊流程：網站折讓待查", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "折讓人工處理－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.MarkManualCompletedAsync(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            ShowAllowanceResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓人工處理未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ConfirmAllowanceCandidateAsync(InvoiceSyncIssue issue, InvoiceRecord record, InvoiceAllowanceManualReview review)
    {
        var candidates = NewAllowanceCandidates(record, review);
        if (candidates.Count <= 1)
        {
            MessageBox.Show(this, "目前沒有多筆可供人工選擇的折讓候選。系統會在每次正常同步時繼續查詢。", "等待折讓確認",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var login = new EmployeeAdminLoginForm(repository, "折讓單號確認－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        using var selector = new AllowanceCandidateForm(candidates);
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedAllowanceNumber.Length == 0) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.ConfirmCandidateAsync(issue, selector.SelectedAllowanceNumber,
                login.EnteredEmployeeNo, login.AuthenticatedPassword);
            ShowAllowanceResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓單號確認未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private void ShowAllowanceResult(InvoiceAllowanceReconcileResult result)
    {
        switch (result.Outcome)
        {
            case InvoiceAllowanceReconcileOutcome.Confirmed:
                MessageBox.Show(this, result.Message, "折讓確認完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case InvoiceAllowanceReconcileOutcome.PendingConfirmation:
                MessageBox.Show(this, result.Message + "\n\n系統會在之後每一次正常同步繼續查詢，不需要重複按「已人工處理」。",
                    "折讓等待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            case InvoiceAllowanceReconcileOutcome.Problem:
                MessageBox.Show(this, result.Message + "\n\n這筆申請仍保留在「上傳問題」，不會自動誤結案。",
                    "折讓資料需要確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void CancelManualReview(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || manualReviewBusy || !IsManualReview(issue)) return;
        var allowance = IsAllowanceManualReview(issue);
        var allowanceVoid = IsAllowanceVoidManualReview(issue);
        var prompt = allowanceVoid
            ? "確定取消並退回這筆折讓作廢申請？\n\n不會對光貿執行折讓作廢。"
            : allowance
                ? "確定取消並退回這筆折讓申請？\n\n不會對光貿執行任何折讓操作。"
                : "確定取消並退回這筆作廢申請？\n\n不會向光貿送出作廢，發票會維持「已開立」。";
        if (MessageBox.Show(this, prompt, "取消退回", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "人工確認－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            if (allowanceVoid)
            {
                allowanceVoidWorkflow.CancelManualReview(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未對光貿執行折讓作廢。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (allowance)
            {
                allowanceWorkflow.CancelManualReview(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未對光貿執行折讓。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                voidWorkflow.CancelManualReview(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未向光貿送出作廢。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "取消退回失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private void UpdateDiagnosticButton()
    {
        exportIssueDiagnostics.Visible = !processing;
        exportIssueDiagnostics.Enabled = !processing && !manualReviewBusy &&
            list.SelectedItems.Count == 1 &&
            list.SelectedItems[0].Tag is InvoiceSyncIssue or InvoiceRecord;
    }

    private void ExportSelectedIssueDiagnostics()
    {
        if (processing || list.SelectedItems.Count != 1) return;
        var selected = list.SelectedItems[0].Tag;
        if (selected is not InvoiceSyncIssue && selected is not InvoiceRecord) return;
        string report;
        string suffix;
        try
        {
            report = selected is InvoiceSyncIssue issue
                ? IssueDiagnosticLog.Export(repository.DataDirectory, issue)
                : IssueDiagnosticLog.ExportFailed((InvoiceRecord)selected!);
            suffix = selected is InvoiceSyncIssue current
                ? current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "failed";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "無法組合診斷：" + error.GetType().Name,
                "診斷失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            using var save = new SaveFileDialog
            {
                Title = "匯出已遮罩問題診斷",
                Filter = "文字檔 (*.txt)|*.txt",
                DefaultExt = "txt",
                AddExtension = true,
                FileName = "CYInvoice-Issue-" + suffix + ".txt"
            };
            if (save.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(save.FileName, report, new System.Text.UTF8Encoding(false));
            MessageBox.Show(this, "已匯出診斷。請先確認內容，再提供給維護人員。",
                "匯出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try
            {
                Clipboard.SetText(report);
                MessageBox.Show(this, "診斷檔案無法儲存，已複製遮罩內容：" +
                    error.GetType().Name, "改以複製提供", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception)
            {
                MessageBox.Show(this, "診斷檔案及剪貼簿均不可用：" +
                    error.GetType().Name, "診斷匯出失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void UpdateCrossDeviceButton()
    {
        if (IsDisposed) return;
        var settings = repository.Settings.LoadOrCreate();
        reportCrossDevice.Visible = !processing && settings.CloudMode != CloudModes.LocalOnly &&
            settings.CloudIdentityProvider == "CYID";
        var selected = SelectedIssue();
        reportCrossDevice.Enabled = reportCrossDevice.Visible && !manualReviewBusy &&
            settings.CloudEmployeeAuthorityReady &&
            settings.CloudWorkspaceId.Length > 0 && settings.CloudDeviceId.Length > 0 &&
            selected is { ResolvedUtc: null } &&
            CloudLocalWorkItemReferences.IsSupportedIssueType(selected.IssueType);
    }

    private async Task ReportCrossDeviceAsync()
    {
        var issue = SelectedIssue();
        if (manualReviewBusy || processing || issue is null || issue.ResolvedUtc is not null) return;
        var settings = repository.Settings.LoadOrCreate();
        if (settings.CloudMode == CloudModes.LocalOnly || settings.CloudIdentityProvider != "CYID" ||
            !settings.CloudEmployeeAuthorityReady || settings.CloudWorkspaceId.Length == 0 ||
            settings.CloudDeviceId.Length == 0 || string.IsNullOrWhiteSpace(settings.CloudBaseUrl)) return;
        if (!issueStore.Unresolved(accountKey).Any(x => x.Id == issue.Id && x.IssueType == issue.IssueType))
        {
            MessageBox.Show(this, "這筆異常已更新或結案，請重新整理。", "回報跨機",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var token = repository.Settings.CloudDeviceToken(settings);
        CloudLocalWorkItemReference source;
        try { source = CloudLocalWorkItemReferences.FromLocalIssue(issue, token); }
        catch (ArgumentException)
        {
            MessageBox.Show(this, "此筆不是 3.1 支援的非折讓例外，無法回報跨機。", "回報跨機",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var purpose = source.Kind == "VOID" ? "未收回紙本作廢覆核" : "上傳問題";
        if (MessageBox.Show(this,
                $"是否回報「{purpose}」給其他 CYID 管理員？\n\n" +
                "僅傳問題類型與不可反查的摘要，不傳發票、買方或訂單資料。\n" +
                "不會執行光貿作廢、重送上傳或變更官方狀態。",
                "回報跨機", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "回報跨機－CYID 當次驗證", administratorRequired: false, collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        UpdateCrossDeviceButton();
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            var client = new CloudWorkItemClient(http,
                new Uri(settings.CloudBaseUrl, UriKind.Absolute), token, settings.CloudWorkspaceId);
            var environment = settings.Environment == Environments.Test ? "test" : "production";
            if (source.Kind == "VOID")
                await client.SubmitVoidReviewAsync(new CloudVoidReviewRequest(
                    source.ItemId, environment, source.ReferenceHash),
                    login.EnteredEmployeeNo, login.AuthenticatedPassword);
            else
                await client.ReportUploadIssueAsync(new CloudUploadIssueReport(
                    source.ItemId, environment, source.ReferenceHash, source.IssueCode!),
                    login.EnteredEmployeeNo, login.AuthenticatedPassword);
            MessageBox.Show(this,
                "已建立跨機人工待辦（非官方作廢／上傳成功）。\n工作項目 ID：" + source.ItemId,
                "回報完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            // Network/DB reply unknown: do NOT send another mutation.
            // The same HMAC-derived ID is available for manager readback.
            var notReady = error is CloudApiException cloud && cloud.Code == "WORK_ITEMS_NOT_READY";
            MessageBox.Show(this,
                notReady ? "3.1 跨機功能尚未啟用，等待本機 MCP 最後升級。"
                    : "結果尚未確認或來源重複，請由管理員依原 ID 查回，勿重送：\n" + source.ItemId,
                "回報跨機", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetManualReviewBusy(false);
            UpdateCrossDeviceButton();
        }
    }

    private InvoiceSyncIssue? SelectedIssue() =>
        list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as InvoiceSyncIssue : null;

    private void SetManualReviewBusy(bool busy)
    {
        manualReviewBusy = busy;
        list.Enabled = !busy;
    }

    private IReadOnlyList<KeyValuePair<string, string>> VoidDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null : voidWorkflow.ManualReviewFor(record);
        var handled = record is null ? null : voidWorkflow.HandledReviewFor(record);
        var parsed = VoidOperationSessionCache.Parse(VoidOperationSessionCache.ReasonFor(issue.InvoiceNumber));
        var requester = review?.RequesterEmployeeNo ?? handled?.RequesterEmployeeNo ?? parsed?.UserEmployeeNo ?? "";
        var requested = review?.RequestedUtc ?? handled?.RequestedUtc;
        rows.Add(new("申請員工", EmployeeDisplay(requester)));
        rows.Add(new("申請時間", requested is null ? "－" : requested.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("作廢原因", review?.Reason ?? handled?.Reason ?? parsed?.Reason ?? "－"));
        rows.Add(new("處理管理員", EmployeeDisplay(handled?.ReviewerEmployeeNo ?? parsed?.ReviewerEmployeeNo ?? "")));
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null ? "已解決" : "等待管理員人工確認"));
        return rows;
    }

    private IReadOnlyList<KeyValuePair<string, string>> AllowanceDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null :
            allowanceWorkflow.ManualReviewFor(record) ??
            (issue.ResolvedUtc is null ? null : allowanceWorkflow.HandledReviewFor(record));
        if (review is not null && Math.Abs((review.RequestedUtc - issue.CreatedUtc).TotalMinutes) > 5)
            review = null;
        rows.Add(new("申請員工", EmployeeDisplay(review?.RequesterEmployeeNo ?? "")));
        rows.Add(new("申請時間", review is null ? "－" : review.RequestedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("折讓原因", review?.Reason ?? "－"));
        if (review?.LocalApprovedUtc is { } approved)
        {
            rows.Add(new("Local 核准管理員", EmployeeDisplay(review.LocalApproverEmployeeNo)));
            rows.Add(new("Local 核准時間", approved.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
            var reservation = issue.ResolvedUtc is null
                ? allowanceWorkflow.LocalReservationFor(issue) : null;
            rows.Add(new("折讓單號", reservation?.AllowanceNumber ?? "尚未預留"));
            rows.Add(new("正式送出", reservation?.Status switch
            {
                null => "草稿已核准，尚未預留／送出",
                "reserved" => "僅預留，尚未呼叫 g0401",
                "sending" => "已進入單次送出，結果未確認；只能依原號查回",
                "confirmed" => "已依官方原號及完整明細核對完成",
                "occupied_external" => "官方明確占號，已鎖定；不得自動換號",
                _ => "未知狀態，禁止操作"
            }));
        }
        rows.Add(new("處理管理員", EmployeeDisplay(review?.HandlerEmployeeNo ?? "")));
        rows.Add(new("人工處理時間", review?.ManualCompletedUtc is { } completed
            ? completed.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") : "－"));
        if (review is not null)
        {
            var taxInclusive = review.TaxInclusiveAmount;
            var tax = taxInclusive - (long)Math.Round(taxInclusive / 1.05D, MidpointRounding.AwayFromZero);
            var untaxed = taxInclusive - tax;
            rows.Add(new("未稅折讓金額", MoneyFormatter.Integer(untaxed)));
            rows.Add(new("稅額", MoneyFormatter.Integer(tax)));
            rows.Add(new("含稅折讓總額", MoneyFormatter.Integer(taxInclusive)));
        }
        else
        {
            rows.Add(new("未稅折讓金額", "－"));
            rows.Add(new("含稅折讓總額", "－"));
        }
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null
            ? "已人工處理"
            : review?.LocalApprovedUtc is not null ? "Local 已核准；請以原號狀態為準"
            : review?.AwaitingConfirmation == true ? "等待光貿確認" : "等待管理員人工處理"));
        if (record is not null)
        {
            rows.Add(new("官方折讓資料", FormatAllowances(InvoiceAllowanceMetadata.ReadOfficial(record))));
            if (review is not null && review.AwaitingConfirmation)
                rows.Add(new("本次新增候選", FormatAllowances(NewAllowanceCandidates(record, review))));
        }
        return rows;
    }

    private IReadOnlyList<KeyValuePair<string, string>> AllowanceVoidDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null : allowanceVoidWorkflow.ManualReviewFor(record);
        rows.Add(new("折讓單號", review?.AllowanceNumber ?? "－"));
        rows.Add(new("申請員工", review?.RequesterEmployeeNo ?? "－"));
        rows.Add(new("申請時間", review is null ? "－" : review.RequestedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("折讓作廢原因", review?.Reason ?? "－"));
        var api = allowanceVoidWorkflow.LocalApprovalFor(issue);
        rows.Add(new("處理狀態", api?.Claimed == true ? "已送出或結果待查；禁止重送／取消" :
            issue.ResolvedUtc is not null ? "已解決" : api is not null ? "已覆核；送出前仍須重新驗證" : "等待管理員覆核"));
        if (api is not null)
        {
            rows.Add(new("核准管理員", api.ManagerEmployeeNo));
            // Existing expires_utc is retained only for compatibility with old approvals.
            rows.Add(new("官方作廢查回", api.LastObservation == "official_void_observed_unattributed"
                ? "原號 D0501／99；尚無法歸因於本次送出" : api.Claimed ? "尚未取得可確認的官方作廢證據" : "尚未送出"));
        }
        return rows;
    }

    private IEnumerable<KeyValuePair<string, string>> CommonDetailRows(InvoiceSyncIssue issue)
    {
        yield return new("類型", DisplayType(issue.IssueType));
        yield return new("發票號碼", issue.InvoiceNumber);
        yield return new("訂單編號", issue.OrderId);
        yield return new("待辦建立時間", issue.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"));
        yield return new("目前訊息", issue.Message);
    }

    private string EmployeeDisplay(string employeeNo)
    {
        employeeNo = employeeNo.Trim();
        if (employeeNo.Length == 0) return "－";
        try
        {
            var account = repository.LoadAuthorityEmployees()
                .FirstOrDefault(employee => string.Equals(employee.EmployeeNo, employeeNo, StringComparison.Ordinal));
            return account is null || string.IsNullOrWhiteSpace(account.Name)
                ? employeeNo : employeeNo + " " + account.Name.Trim();
        }
        catch
        {
            return employeeNo;
        }
    }

    private InvoiceRecord FindIssueRecord(InvoiceSyncIssue issue) =>
        TryFindIssueRecord(issue) ?? throw new InvalidOperationException("本機找不到這筆人工確認對應的發票");

    private InvoiceRecord? TryFindIssueRecord(InvoiceSyncIssue issue)
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        var matches = repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 || string.Equals(record.SellerInvoice.Trim(), sellerInvoice, StringComparison.Ordinal))
            .Where(record =>
                (issue.InvoiceNumber.Trim().Length != 0 && string.Equals(record.InvoiceNumber.Trim(), issue.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (issue.OrderId.Trim().Length != 0 && string.Equals(EffectiveOrderId(record), issue.OrderId.Trim(), StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length > 1) throw new InvalidDataException("人工確認對到多筆本機發票，已停止處理");
        return matches.SingleOrDefault();
    }

    private static IReadOnlyList<InvoiceAllowanceResult> NewAllowanceCandidates(InvoiceRecord record, InvoiceAllowanceManualReview review)
    {
        var baseline = new HashSet<string>(review.BaselineAllowanceNumbers ?? [], StringComparer.OrdinalIgnoreCase);
        return InvoiceAllowanceMetadata.ReadOfficial(record)
            .Where(item => item.AllowanceNumber.Trim().Length != 0)
            .Where(item => !baseline.Contains(item.AllowanceNumber.Trim()))
            .ToArray();
    }

    private static string FormatAllowances(IReadOnlyList<InvoiceAllowanceResult> values)
    {
        if (values.Count == 0) return "無";
        return string.Join("\r\n", values.Select(item =>
        {
            var amount = "?";
            try { amount = FixedDecimal.Add(FixedDecimal.Parse(item.TotalAmount), FixedDecimal.Parse(item.TaxAmount)).ToString(); }
            catch (Exception error) when (error is FormatException or OverflowException) { }
            return $"{item.AllowanceNumber}｜{item.AllowanceDate}｜{item.InvoiceType}｜狀態 {item.InvoiceStatus}｜含稅 {amount}";
        }));
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private void DeleteCheckedFailed()
    {
        var records = list.CheckedItems.Cast<ListViewItem>().Select(item => item.Tag).OfType<InvoiceRecord>().ToArray();
        if (records.Length == 0 || !ConfirmFailedDeletion(records.Length, clearAll: false)) return;
        DeleteFailedRecords(records);
    }

    private void ClearAllFailed()
    {
        var records = CurrentFailedRecords().ToArray();
        if (records.Length == 0 || !ConfirmFailedDeletion(records.Length, clearAll: true)) return;
        DeleteFailedRecords(records);
    }

    private bool ConfirmFailedDeletion(int count, bool clearAll) => MessageBox.Show(
        this,
        clearAll
            ? $"確定清除全部 {count} 筆開立失敗紀錄？\n\n此操作只清除 CYInvoice 本機的開立失敗紀錄，不影響上傳問題及光貿資料。"
            : $"確定刪除勾選的 {count} 筆開立失敗紀錄？\n\n此操作只刪除 CYInvoice 本機紀錄，不會影響光貿資料。",
        clearAll ? "清除開立失敗紀錄" : "刪除開立失敗紀錄",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void DeleteFailedRecords(IReadOnlyCollection<InvoiceRecord> records)
    {
        try
        {
            failedStore.Delete(records);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "刪除開立失敗紀錄失敗：" + error.Message, "刪除失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IReadOnlyList<InvoiceRecord> CurrentFailedRecords()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        return repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 || string.Equals(record.SellerInvoice.Trim(), sellerInvoice, StringComparison.Ordinal))
            .Where(record => string.Equals(record.InvoiceState, InvoiceStates.Failed, StringComparison.Ordinal))
            .OrderByDescending(record => FullIssueTime(record), StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPendingAllowanceStatus(int status) => status is
        UploadStatuses.Pending or UploadStatuses.Uploading or UploadStatuses.Uploaded or UploadStatuses.Processing or UploadStatuses.Confirming;

    private void UpdateFailedButtons()
    {
        if (IsDisposed) return;
        var count = list.CheckedItems.Cast<ListViewItem>().Count(item => item.Tag is InvoiceRecord);
        deleteFailed.Text = count == 0 ? "刪除勾選" : $"刪除勾選 ({count})";
        deleteFailed.Enabled = count > 0;
        clearFailed.Enabled = list.Items.Cast<ListViewItem>().Any(item => item.Tag is InvoiceRecord);
    }

    private string IssueState(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null)
            return IsAllowanceManualReview(issue) || IsAllowanceVoidManualReview(issue) ? "已人工處理" : "已解決";
        if (administrativeClosure.IsActiveWorkIssue(issue))
        {
            try { if (administrativeClosure.CanClose(issue)) return "可結案"; }
            catch { }
        }
        if (IsAllowanceManualReview(issue))
        {
            try
            {
                var record = FindIssueRecord(issue);
                return allowanceWorkflow.ManualReviewFor(record)?.AwaitingConfirmation == true ? "等待確認" : "人工確認";
            }
            catch { return "人工確認"; }
        }
        if (IsVoidManualReview(issue) || IsAllowanceVoidManualReview(issue)) return "人工確認";
        return lastRead is not null && issue.CreatedUtc <= lastRead.Value ? "已讀" : "未讀";
    }

    private static bool IsManualReview(InvoiceSyncIssue issue) =>
        IsVoidManualReview(issue) || IsAllowanceManualReview(issue) || IsAllowanceVoidManualReview(issue);

    private static bool IsVoidManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceVoidIssueTypes.ManualReview, StringComparison.Ordinal);

    private static bool IsAllowanceManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceAllowanceIssueTypes.ManualReview, StringComparison.Ordinal);

    private static bool IsAllowanceVoidManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceAllowanceVoidIssueTypes.ManualReview, StringComparison.Ordinal);

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0) throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return settings.Environment + "|" + sellerInvoice;
    }

    private void LayoutColumns()
    {
        if (list.Columns.Count != DefaultWidths.Length || list.ClientSize.Width <= 0) return;
        var widths = DefaultWidths.ToArray();
        var available = Math.Max(1, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        widths[4] = Math.Max(60, available - widths.Where((_, index) => index != 4).Sum());
        settingColumnWidths = true;
        try
        {
            for (var index = 0; index < widths.Length; index++)
                if (list.Columns[index].Width != widths[index]) list.Columns[index].Width = widths[index];
        }
        finally { settingColumnWidths = false; }
    }

    private void ShowClippedContentToolTip(object? sender, MouseEventArgs eventArgs)
    {
        var hit = list.HitTest(eventArgs.Location);
        var content = hit.Item is { } item && item.SubItems.Count > 4 &&
            ReferenceEquals(hit.SubItem, item.SubItems[4])
            ? item.SubItems[4].Text : string.Empty;
        var available = list.Columns[4].Width - 12;
        if (content.Length == 0 || TextRenderer.MeasureText(content, list.Font).Width <= available)
            content = string.Empty;
        if (content == visibleToolTip) return;
        visibleToolTip = content;
        contentToolTip.SetToolTip(list, content);
    }

    private void ClearContentToolTip()
    {
        visibleToolTip = string.Empty;
        contentToolTip.SetToolTip(list, string.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            contentToolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void ApplyZebra(ListViewItem row, int index)
    {
        row.UseItemStyleForSubItems = false;
        var background = index % 2 == 0 ? Color.White : Color.FromArgb(238, 244, 250);
        foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
        {
            subItem.BackColor = background;
            if (subItem.ForeColor == Color.Empty) subItem.ForeColor = SystemColors.ControlText;
        }
    }

    private static string FullIssueTime(InvoiceRecord record) =>
        record.InvoiceDate.Trim().Length != 0 ? (record.InvoiceDate.Trim() + " " + record.InvoiceTime.Trim()).Trim() : record.SentAt.Trim();

    private static string ShortIssueTime(string value) => value.Length >= 16 ? value[..16] : value;

    private static string FriendlyFailureReason(string raw)
    {
        var message = raw.Trim();
        if (message.Length == 0) return "光貿未提供失敗原因";
        if (message.StartsWith("API code ", StringComparison.OrdinalIgnoreCase))
        {
            var colon = message.IndexOf(':');
            if (colon >= 0 && colon + 1 < message.Length) message = message[(colon + 1)..].Trim();
        }
        var field = FailureField(message);
        if (field.Length != 0)
        {
            if (ContainsAny(message, "required", "empty", "blank", "不可空", "不得空", "不能空")) return field + "不可空白";
            if (ContainsAny(message, "length", "長度")) return field + "長度不正確";
            if (ContainsAny(message, "duplicate", "already exists", "重複", "已存在")) return field + "重複";
            return field + "資料格式不正確";
        }
        if (message.Any(IsCjk)) return message;
        return "光貿拒絕此筆開立資料，請檢查發票內容";
    }

    private static string FailureField(string message)
    {
        var fields = new (string Technical, string Display)[]
        {
            ("BuyerIdentifier", "買受人統編"), ("BuyerName", "買受人名稱"), ("BuyerAddress", "買受人地址"),
            ("BuyerTelephoneNumber", "買受人電話"), ("BuyerEmailAddress", "買受人電子郵件"),
            ("OrderId", "訂單編號"), ("OrderID", "訂單編號"), ("SalesAmount", "銷售額"),
            ("TaxAmount", "稅額"), ("TotalAmount", "發票總額"), ("ProductItem", "商品明細"),
            ("Description", "商品名稱"), ("Quantity", "商品數量"), ("UnitPrice", "商品單價"),
            ("CarrierType", "載具類型"), ("CarrierId1", "載具號碼"), ("CarrierId2", "載具號碼"),
            ("NPOBAN", "捐贈碼"), ("MainRemark", "備註"), ("DetailVat", "明細含稅設定"),
        };
        foreach (var field in fields)
            if (message.Contains(field.Technical, StringComparison.OrdinalIgnoreCase)) return field.Display;
        return string.Empty;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    internal void VerifySmokeFailedRecord(string recordId)
    {
        if (!list.Items.Cast<ListViewItem>().Any(row => row.Tag is InvoiceRecord record && record.Id == recordId))
            throw new InvalidOperationException("開立失敗紀錄未保留在上傳問題清單");
    }

    internal void VerifySmokeLayout()
    {
        if (Text != (processing ? "處理中" : "上傳問題") || ShowIcon || Math.Abs(Font.SizeInPoints - 10F) > 0.1F)
            throw new InvalidOperationException("上傳問題視窗標題、圖示或字級不正確");
        if (!listHost.UsesOnlyNativeScrollBar || list.CheckBoxes == processing || list.Columns.Count != 6 || !list.GridLines || !list.Scrollable)
            throw new InvalidOperationException("上傳問題單一清單結構不正確");
        if (list.Columns[5].Text != "狀態" || clearFailed.Text != "清除開立失敗紀錄" ||
            (!processing && exportIssueDiagnostics.Text != "匯出問題診斷"))
            throw new InvalidOperationException("上傳問題欄位或開立失敗清除按鈕不正確");
        if (Descendants(this).OfType<Button>().Any(button => button.Text is "標記已解決" or "查看明細" or "確認送出作廢" or "取消退回"))
            throw new InvalidOperationException("上傳問題主視窗仍保留待辦操作按鈕，操作應位於雙擊後的詳細視窗");
        if (FriendlyFailureReason("API code 3040122: BuyerIdentifier invalid") != "買受人統編資料格式不正確")
            throw new InvalidOperationException("開立失敗原因未轉為可理解中文");
        if (LocalAllowancePrimaryAction("occupied_external") !=
                "繼續自動加號並送出" ||
            LocalAllowancePrimaryAction("sending") !=
                "依原折讓號碼唯讀查回" ||
            LocalAllowancePrimaryAction("confirmed") != string.Empty)
            throw new InvalidOperationException(
                "Local allowance must not bypass reviewed collision or ambiguous-send UI.");
        if (LegacyAllowanceWebsiteAction != "舊流程：網站已人工折讓" ||
            LegacyAllowanceVoidWebsiteAction != "舊流程：網站已作廢")
            throw new InvalidOperationException("G 階段舊網站入口必須與新單次送出明確區分");
        if (DisplayType(InvoiceVoidIssueTypes.ManualReview) != "紙本作廢確認" ||
            DisplayType(InvoiceVoidSyncIssueTypes.PendingConfirmation) != "作廢結果待確認" ||
            DisplayType(InvoiceAllowanceIssueTypes.ManualReview) != "折讓人工處理" ||
            DisplayType(InvoiceAllowanceVoidIssueTypes.ManualReview) != "折讓作廢人工處理")
            throw new InvalidOperationException("人工確認問題類型顯示不正確");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    // Keep old website paths visibly distinct from the new authorized
    // g0401/g0501 workflow until real financial acceptance permits removal.
    // These labels do not authorize a financial send or mark AMEGO success.
    internal const string LegacyAllowanceWebsiteAction = "舊流程：網站已人工折讓";
    internal const string LegacyAllowanceVoidWebsiteAction = "舊流程：網站已作廢";

    private static string DisplayType(string type) => type switch
    {
        InvoiceSyncIssueTypes.InvoiceListFailed => "發票清單查詢失敗",
        InvoiceSyncIssueTypes.AllowanceLimitExceeded => "折讓每日查詢超限",
        InvoiceSyncIssueTypes.QueryFailed => "發票回查失敗",
        InvoiceSyncIssueTypes.RemoteNotFound => "光貿查無發票",
        InvoiceSyncIssueTypes.UnknownRemoteNotFound => "結果不明且光貿查無",
        InvoiceSyncIssueTypes.ReconciliationFailed => "同步比對失敗",
        InvoiceSyncIssueTypes.AmbiguousMatch => "本機資料無法唯一對應",
        InvoiceSyncIssueTypes.LocalWriteFailed => "本機資料庫寫入失敗",
        InvoiceVoidIssueTypes.ManualReview => "紙本作廢確認",
        InvoiceVoidSyncIssueTypes.PendingConfirmation => "作廢結果待確認",
        InvoiceAllowanceIssueTypes.ManualReview => "折讓人工處理",
        InvoiceAllowanceVoidIssueTypes.ManualReview => "折讓作廢人工處理",
        _ => type,
    };

}
