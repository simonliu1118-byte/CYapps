using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SyncIssuesForm : Form
{
    internal const string ReadStateScope = "upload-issues-read";

    private static readonly int[] IssueDefaultWidths = [130, 145, 105, 135, 200, 70];
    private static readonly int[] FailedDefaultWidths = [135, 60, 145, 125, 80, 200];

    private readonly LocalRepository repository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly FailedInvoiceRecordStore failedStore;
    private readonly EmployeeVoidWorkflowService voidWorkflow;
    private readonly EmployeeAllowanceWorkflowService allowanceWorkflow;
    private readonly InvoiceAdministrativeClosureService administrativeClosure;
    private readonly string accountKey;
    private DateTimeOffset? lastRead;
    private bool manualReviewBusy;

    private readonly ListView issueList = new BufferedListView
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        BorderStyle = BorderStyle.FixedSingle,
        GridLines = true,
        Scrollable = true,
    };
    private readonly ListView failedList = new BufferedListView
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        HideSelection = false,
        CheckBoxes = true,
        BorderStyle = BorderStyle.FixedSingle,
        GridLines = true,
        Scrollable = true,
    };
    private readonly Label summary = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Anchor = AnchorStyles.Left,
    };
    private readonly Button deleteFailed = UiControls.StandardButton("刪除");
    private readonly Button deleteAllFailed = UiControls.StandardButton("全部刪除");

    public SyncIssuesForm(LocalRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        failedStore = new FailedInvoiceRecordStore(repository.DataDirectory);
        voidWorkflow = new EmployeeVoidWorkflowService(repository);
        allowanceWorkflow = new EmployeeAllowanceWorkflowService(repository);
        administrativeClosure = new InvoiceAdministrativeClosureService(repository);
        accountKey = CurrentAccountKey();
        lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);

        Text = "上傳問題";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 450);
        ClientSize = new Size(840, 500);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.White;
        ShowIcon = false;

        BuildLayout();
        MarkVisibleIssuesRead();
        ReloadAll();
    }

    private void BuildLayout()
    {
        ConfigureIssueList();
        ConfigureFailedList();

        var reload = UiControls.StandardButton("重新整理");
        reload.Click += (_, _) => ReloadAll();
        issueList.DoubleClick += async (_, _) => await ShowSelectedIssueDetailsAsync();

        deleteFailed.Enabled = false;
        deleteFailed.Click += (_, _) => DeleteCheckedFailed();
        deleteAllFailed.Enabled = false;
        deleteAllFailed.Click += (_, _) => DeleteAllFailed();
        failedList.ItemChecked += (_, _) => BeginInvoke((Action)UpdateFailedButtons);

        var close = UiControls.StandardButton("關閉");
        close.Click += (_, _) => Close();

        var issueHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
        };
        issueHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        issueHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        issueHeader.Controls.Add(summary, 0, 0);
        reload.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        issueHeader.Controls.Add(reload, 1, 0);

        var failedHeader = new Label
        {
            Text = "開立失敗",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 70, 70),
        };

        var bottomActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
        };
        bottomActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottomActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var failedActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        failedActions.Controls.Add(deleteFailed);
        failedActions.Controls.Add(deleteAllFailed);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Margin = new Padding(0, 4, 0, 0);
        bottomActions.Controls.Add(failedActions, 0, 0);
        bottomActions.Controls.Add(close, 1, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
            Tag = "upload-issues-root",
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(issueHeader, 0, 0);
        root.Controls.Add(issueList, 0, 1);
        root.Controls.Add(failedHeader, 0, 2);
        root.Controls.Add(failedList, 0, 3);
        root.Controls.Add(bottomActions, 0, 4);
        Controls.Add(root);

        Resize += (_, _) => LayoutColumns();
    }

    private void ConfigureIssueList()
    {
        issueList.Columns.Add("時間", IssueDefaultWidths[0], HorizontalAlignment.Left);
        issueList.Columns.Add("類型", IssueDefaultWidths[1], HorizontalAlignment.Left);
        issueList.Columns.Add("發票號碼", IssueDefaultWidths[2], HorizontalAlignment.Left);
        issueList.Columns.Add("訂單編號", IssueDefaultWidths[3], HorizontalAlignment.Left);
        issueList.Columns.Add("內容", IssueDefaultWidths[4], HorizontalAlignment.Left);
        issueList.Columns.Add("狀態", IssueDefaultWidths[5], HorizontalAlignment.Center);
    }

    private void ConfigureFailedList()
    {
        failedList.Columns.Add("時間", FailedDefaultWidths[0], HorizontalAlignment.Left);
        failedList.Columns.Add("來源", FailedDefaultWidths[1], HorizontalAlignment.Left);
        failedList.Columns.Add("訂單編號", FailedDefaultWidths[2], HorizontalAlignment.Left);
        failedList.Columns.Add("買受人", FailedDefaultWidths[3], HorizontalAlignment.Left);
        failedList.Columns.Add("金額", FailedDefaultWidths[4], HorizontalAlignment.Right);
        failedList.Columns.Add("失敗原因", FailedDefaultWidths[5], HorizontalAlignment.Left);
    }

    private void ReloadAll()
    {
        ReloadIssues();
        ReloadFailed();
        LayoutColumns();
    }

    private void ReloadIssues()
    {
        try
        {
            lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);
            var issues = issueStore.All(accountKey);
            issueList.BeginUpdate();
            try
            {
                issueList.Items.Clear();
                var rowIndex = 0;
                foreach (var issue in issues)
                {
                    var state = IssueState(issue);
                    var row = new ListViewItem(issue.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"));
                    row.SubItems.Add(DisplayType(issue.IssueType));
                    row.SubItems.Add(issue.InvoiceNumber);
                    row.SubItems.Add(issue.OrderId);
                    row.SubItems.Add(issue.Message);
                    row.SubItems.Add(state);
                    row.Tag = issue;
                    ApplyZebra(row, rowIndex++);
                    var stateCell = row.SubItems[5];
                    stateCell.ForeColor = state switch
                    {
                        "未讀" => Color.Firebrick,
                        "人工確認" or "等待確認" or "可結案" => Color.FromArgb(190, 120, 0),
                        "已解決" => Color.FromArgb(0, 132, 72),
                        _ => Color.DimGray,
                    };
                    if (state is "人工確認" or "等待確認" or "可結案")
                        stateCell.Font = new Font(issueList.Font, FontStyle.Bold);
                    issueList.Items.Add(row);
                }
            }
            finally
            {
                issueList.EndUpdate();
            }

            var unresolved = issues.Count(issue => issue.ResolvedUtc is null);
            summary.Text = unresolved == 0 ? "目前沒有尚未解決的上傳問題" : $"尚未解決：{unresolved} 項";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "讀取上傳問題失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadFailed()
    {
        try
        {
            var records = CurrentFailedRecords();
            failedList.BeginUpdate();
            try
            {
                failedList.Items.Clear();
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    var row = new ListViewItem(FullIssueTime(record));
                    row.SubItems.Add(InvoiceSourceInference.Display(record));
                    row.SubItems.Add(record.OrderId);
                    row.SubItems.Add(record.BuyerName);
                    row.SubItems.Add(MoneyFormatter.Integer(record.Amount));
                    row.SubItems.Add(FriendlyFailureReason(record.ErrorMessage));
                    row.Tag = record;
                    ApplyZebra(row, index);
                    failedList.Items.Add(row);
                }
            }
            finally
            {
                failedList.EndUpdate();
            }
            UpdateFailedButtons();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "讀取開立失敗紀錄失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        if (manualReviewBusy || SelectedIssue() is not { } issue) return;

        try
        {
            var manual = IsManualReview(issue);
            var rows = manual
                ? IsAllowanceManualReview(issue) ? AllowanceDetailRows(issue) : VoidDetailRows(issue)
                : CommonDetailRows(issue).Append(new("處理狀態", issue.ResolvedUtc is null ? IssueState(issue) : "已解決")).ToArray();

            var primaryText = string.Empty;
            var allowCancel = false;
            var allowAdministrativeClose = false;
            var primaryDanger = false;

            if (issue.ResolvedUtc is null)
            {
                if (InvoiceAdministrativeClosureService.IsAdministrativeClosureIssue(issue))
                {
                    try { allowAdministrativeClose = administrativeClosure.CanClose(issue); }
                    catch { allowAdministrativeClose = false; }
                }

                if (!allowAdministrativeClose && IsVoidManualReview(issue))
                {
                    primaryText = "確認送出作廢";
                    primaryDanger = true;
                    allowCancel = true;
                }
                else if (!allowAdministrativeClose && IsAllowanceManualReview(issue))
                {
                    var record = FindIssueRecord(issue);
                    var review = allowanceWorkflow.ManualReviewFor(record);
                    if (review is not null)
                    {
                        if (!review.AwaitingConfirmation)
                        {
                            primaryText = "已解決";
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
                else if (!allowAdministrativeClose && !manual)
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
                primaryDanger);
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
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "讀取待辦明細失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResolveIssue(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || IsManualReview(issue) || InvoiceAdministrativeClosureService.IsAdministrativeClosureIssue(issue)) return;
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
        if (MessageBox.Show(
                this,
                $"這筆{operation}待處理作業已超過系統保留的兩期。\n\n管理員結案後，CYInvoice 會停止追蹤這筆作業、清除本機等待標記，之後可依舊資料保留規則清除。\n\n此操作不會呼叫光貿 API，也不代表光貿已完成{operation}。確定結案？",
                "管理員結案",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository.Employees, "管理員結案－驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        SetManualReviewBusy(true);
        try
        {
            administrativeClosure.Close(issue, login.AuthenticatedEmployee.EmployeeNo, login.AuthenticatedPassword);
            MessageBox.Show(this, "已由管理員手動結案。CYInvoice 不會再自動追蹤這筆待處理作業。", "結案完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "管理員結案失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetManualReviewBusy(false);
        }
    }

    private async Task ProcessManualReviewAsync(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || manualReviewBusy) return;
        if (IsVoidManualReview(issue))
        {
            await ApproveVoidManualReviewAsync(issue);
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

        if (!review.AwaitingConfirmation)
            await CompleteAllowanceAsync(issue);
        else
            await ConfirmAllowanceCandidateAsync(issue, record, review);
    }

    private async Task ApproveVoidManualReviewAsync(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(
                this,
                "這筆作廢申請的紙本電子發票證明聯先前尚未收回。\n\n確認後，CYInvoice 會重新查詢光貿最新狀態，再送出作廢。請確認已完成主管核准並可進行作廢。",
                "確認送出作廢",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository.Employees, "人工確認－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        SetManualReviewBusy(true);
        try
        {
            var result = await voidWorkflow.ApproveManualReviewAsync(issue, login.AuthenticatedEmployee.EmployeeNo, login.AuthenticatedPassword);
            switch (result.Outcome)
            {
                case InvoiceVoidOutcome.Confirmed:
                case InvoiceVoidOutcome.AlreadyVoided:
                    MessageBox.Show(this, "光貿已確認發票作廢完成。", "作廢完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case InvoiceVoidOutcome.PendingConfirmation:
                    MessageBox.Show(this,
                        "作廢已送出，但光貿尚未確認最終結果。\n\n發票目前為「已開立 (等待作廢)」；後續每次正常同步都會再查詢官方結果。",
                        "作廢結果待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.Rejected:
                    MessageBox.Show(this, result.Message + "\n\n人工確認仍保留，尚未標記完成。", "作廢未完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        finally
        {
            SetManualReviewBusy(false);
        }
    }

    private async Task CompleteAllowanceAsync(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(
                this,
                "請先確認已在光貿網站完成這筆人工折讓。\n\n按下「是」後，CYInvoice 會進入等待確認並用 invoice_query 自動比對新的折讓資料。",
                "折讓已解決",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository.Employees, "折讓人工處理－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.MarkManualCompletedAsync(issue, login.AuthenticatedEmployee.EmployeeNo, login.AuthenticatedPassword);
            ShowAllowanceResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓人工處理未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetManualReviewBusy(false);
        }
    }

    private async Task ConfirmAllowanceCandidateAsync(
        InvoiceSyncIssue issue,
        InvoiceRecord record,
        InvoiceAllowanceManualReview review)
    {
        var candidates = NewAllowanceCandidates(record, review);
        if (candidates.Count <= 1)
        {
            MessageBox.Show(this, "目前沒有多筆可供人工選擇的折讓候選。系統會在每次正常同步時繼續查詢。", "等待折讓確認",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var login = new EmployeeAdminLoginForm(repository.Employees, "折讓單號確認－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;
        using var selector = new AllowanceCandidateForm(candidates);
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedAllowanceNumber.Length == 0) return;

        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.ConfirmCandidateAsync(
                issue,
                selector.SelectedAllowanceNumber,
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword);
            ShowAllowanceResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓單號確認未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetManualReviewBusy(false);
        }
    }

    private void ShowAllowanceResult(InvoiceAllowanceReconcileResult result)
    {
        switch (result.Outcome)
        {
            case InvoiceAllowanceReconcileOutcome.Confirmed:
                MessageBox.Show(this, result.Message, "折讓確認完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case InvoiceAllowanceReconcileOutcome.PendingConfirmation:
                MessageBox.Show(this,
                    result.Message + "\n\n系統會在之後每一次正常同步繼續查詢，不需要重複按「已解決」。",
                    "折讓等待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            case InvoiceAllowanceReconcileOutcome.Problem:
                MessageBox.Show(this,
                    result.Message + "\n\n這筆申請仍保留在「上傳問題」，不會自動誤結案。",
                    "折讓資料需要確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void CancelManualReview(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || manualReviewBusy || !IsManualReview(issue)) return;
        var allowance = IsAllowanceManualReview(issue);
        var prompt = allowance
            ? "確定取消並退回這筆折讓申請？\n\n不會對光貿執行任何折讓操作。"
            : "確定取消並退回這筆作廢申請？\n\n不會向光貿送出作廢，發票會維持「已開立」。";
        if (MessageBox.Show(this, prompt, "取消退回", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository.Employees, "人工確認－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        SetManualReviewBusy(true);
        try
        {
            if (allowance)
            {
                allowanceWorkflow.CancelManualReview(issue, login.AuthenticatedEmployee.EmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未對光貿執行折讓。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                voidWorkflow.CancelManualReview(issue, login.AuthenticatedEmployee.EmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未向光貿送出作廢。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "取消退回失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetManualReviewBusy(false);
        }
    }

    private InvoiceSyncIssue? SelectedIssue() =>
        issueList.SelectedItems.Count == 1 ? issueList.SelectedItems[0].Tag as InvoiceSyncIssue : null;

    private void SetManualReviewBusy(bool busy)
    {
        manualReviewBusy = busy;
        issueList.Enabled = !busy;
    }

    private IReadOnlyList<KeyValuePair<string, string>> VoidDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null : voidWorkflow.ManualReviewFor(record);
        rows.Add(new("申請員工", review?.RequesterEmployeeNo ?? "－"));
        rows.Add(new("申請時間", review is null ? "－" : review.RequestedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("作廢原因", review?.Reason ?? "－"));
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null ? "已解決" : "等待管理員人工確認"));
        return rows;
    }

    private IReadOnlyList<KeyValuePair<string, string>> AllowanceDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null : allowanceWorkflow.ManualReviewFor(record);
        rows.Add(new("申請員工", review?.RequesterEmployeeNo ?? "－"));
        rows.Add(new("申請時間", review is null ? "－" : review.RequestedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("折讓原因", review?.Reason ?? "－"));
        rows.Add(new("含稅折讓總額", review is null ? "－" : MoneyFormatter.Integer(review.TaxInclusiveAmount)));
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null
            ? "已解決"
            : review?.AwaitingConfirmation == true ? "等待光貿確認" : "等待管理員人工處理"));
        if (record is not null)
        {
            rows.Add(new("官方折讓資料", FormatAllowances(InvoiceAllowanceMetadata.ReadOfficial(record))));
            if (review is not null && review.AwaitingConfirmation)
                rows.Add(new("本次新增候選", FormatAllowances(NewAllowanceCandidates(record, review))));
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
            try
            {
                amount = FixedDecimal.Add(FixedDecimal.Parse(item.TotalAmount), FixedDecimal.Parse(item.TaxAmount)).ToString();
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
            }
            return $"{item.AllowanceNumber}｜{item.AllowanceDate}｜{item.InvoiceType}｜狀態 {item.InvoiceStatus}｜含稅 {amount}";
        }));
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private void DeleteCheckedFailed()
    {
        var records = failedList.CheckedItems.Cast<ListViewItem>().Select(item => item.Tag).OfType<InvoiceRecord>().ToArray();
        if (records.Length == 0 || !ConfirmFailedDeletion(records.Length)) return;
        DeleteFailedRecords(records);
    }

    private void DeleteAllFailed()
    {
        var records = CurrentFailedRecords().ToArray();
        if (records.Length == 0 || !ConfirmFailedDeletion(records.Length)) return;
        DeleteFailedRecords(records);
    }

    private bool ConfirmFailedDeletion(int count) => MessageBox.Show(
        this,
        $"確定刪除 {count} 筆開立失敗紀錄？\n\n此操作只刪除 CYInvoice 本機紀錄，不會影響光貿資料。",
        "刪除開立失敗紀錄",
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
        var count = failedList.CheckedItems.Count;
        deleteFailed.Text = count == 0 ? "刪除" : $"刪除({count})";
        deleteFailed.Enabled = count > 0;
        deleteAllFailed.Enabled = failedList.Items.Count > 0;
    }

    private string IssueState(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null) return "已解決";
        if (InvoiceAdministrativeClosureService.IsAdministrativeClosureIssue(issue))
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
        if (IsVoidManualReview(issue)) return "人工確認";
        return lastRead is not null && issue.CreatedUtc <= lastRead.Value ? "已讀" : "未讀";
    }

    private static bool IsManualReview(InvoiceSyncIssue issue) => IsVoidManualReview(issue) || IsAllowanceManualReview(issue);

    private static bool IsVoidManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceVoidIssueTypes.ManualReview, StringComparison.Ordinal);

    private static bool IsAllowanceManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceAllowanceIssueTypes.ManualReview, StringComparison.Ordinal);

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0) throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return settings.Environment + "|" + sellerInvoice;
    }

    private void LayoutColumns()
    {
        SizeColumns(issueList, IssueDefaultWidths, flexibleColumn: 4, checkboxFirstColumn: false);
        SizeColumns(failedList, FailedDefaultWidths, flexibleColumn: 5, checkboxFirstColumn: true);
    }

    private static void SizeColumns(ListView list, IReadOnlyList<int> defaults, int flexibleColumn, bool checkboxFirstColumn)
    {
        if (list.Columns.Count != defaults.Count || list.ClientSize.Width <= 0) return;
        var widths = defaults.ToArray();
        for (var column = 0; column < list.Columns.Count; column++)
        {
            var measured = TextRenderer.MeasureText(list.Columns[column].Text, list.Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 18;
            if (checkboxFirstColumn && column == 0) measured += 22;
            foreach (ListViewItem item in list.Items)
            {
                if (column >= item.SubItems.Count) continue;
                var valueWidth = TextRenderer.MeasureText(item.SubItems[column].Text, item.SubItems[column].Font ?? list.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 18;
                if (checkboxFirstColumn && column == 0) valueWidth += 22;
                measured = Math.Max(measured, valueWidth);
            }
            widths[column] = Math.Max(widths[column], measured);
        }
        var available = Math.Max(0, list.ClientSize.Width - 2);
        var total = widths.Sum();
        if (total < available) widths[flexibleColumn] += available - total;
        for (var column = 0; column < widths.Length; column++) list.Columns[column].Width = widths[column];
    }

    private static void ApplyZebra(ListViewItem row, int index)
    {
        row.UseItemStyleForSubItems = false;
        var background = index % 2 == 0 ? Color.White : Color.FromArgb(238, 244, 250);
        foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
        {
            subItem.BackColor = background;
            subItem.ForeColor = SystemColors.ControlText;
        }
    }

    private static string FullIssueTime(InvoiceRecord record) =>
        record.InvoiceDate.Trim().Length != 0 ? (record.InvoiceDate.Trim() + " " + record.InvoiceTime.Trim()).Trim() : record.SentAt.Trim();

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

    internal void VerifySmokeLayout()
    {
        if (Text != "上傳問題" || ShowIcon || Math.Abs(Font.SizeInPoints - 10F) > 0.1F)
            throw new InvalidOperationException("上傳問題視窗標題、圖示或字級不正確");
        if (issueList is not BufferedListView || failedList is not BufferedListView)
            throw new InvalidOperationException("上傳問題清單未使用雙緩衝 ListView");
        if (issueList.CheckBoxes || !failedList.CheckBoxes || issueList.Columns.Count != 6 || failedList.Columns.Count != 6)
            throw new InvalidOperationException("上傳問題上下清單結構不正確");
        if (!issueList.GridLines || !failedList.GridLines || !issueList.Scrollable || !failedList.Scrollable)
            throw new InvalidOperationException("上傳問題上下清單未保留直向格線或水平捲動能力");
        if (issueList.Columns[5].Text != "狀態")
            throw new InvalidOperationException("上傳問題狀態欄未位於最右側");
        if (deleteFailed.Text != "刪除")
            throw new InvalidOperationException("未勾選開立失敗紀錄時刪除按鈕不應顯示 (0)");
        if (Descendants(this).OfType<Button>().Any(button => button.Text is "標記已解決" or "查看明細" or "確認送出作廢" or "取消退回"))
            throw new InvalidOperationException("上傳問題主視窗仍保留待辦操作按鈕，操作應位於雙擊後的詳細視窗");
        if (FriendlyFailureReason("API code 3040122: BuyerIdentifier invalid") != "買受人統編資料格式不正確")
            throw new InvalidOperationException("開立失敗原因未轉為可理解中文");
        if (DisplayType(InvoiceVoidIssueTypes.ManualReview) != "紙本作廢確認" ||
            DisplayType(InvoiceVoidSyncIssueTypes.PendingConfirmation) != "作廢結果待確認" ||
            DisplayType(InvoiceAllowanceIssueTypes.ManualReview) != "折讓人工處理")
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

    private static string DisplayType(string type) => type switch
    {
        InvoiceSyncIssueTypes.InvoiceListFailed => "發票清單查詢失敗",
        InvoiceSyncIssueTypes.QueryFailed => "發票回查失敗",
        InvoiceSyncIssueTypes.RemoteNotFound => "光貿查無發票",
        InvoiceSyncIssueTypes.UnknownRemoteNotFound => "結果不明且光貿查無",
        InvoiceSyncIssueTypes.ReconciliationFailed => "同步比對失敗",
        InvoiceSyncIssueTypes.AmbiguousMatch => "本機資料無法唯一對應",
        InvoiceSyncIssueTypes.LocalWriteFailed => "本機資料庫寫入失敗",
        InvoiceVoidIssueTypes.ManualReview => "紙本作廢確認",
        InvoiceVoidSyncIssueTypes.PendingConfirmation => "作廢結果待確認",
        InvoiceAllowanceIssueTypes.ManualReview => "折讓人工處理",
        _ => type,
    };

    private sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }
}
