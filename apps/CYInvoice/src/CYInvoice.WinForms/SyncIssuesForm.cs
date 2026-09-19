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
    private readonly string accountKey;
    private DateTimeOffset? lastRead;
    private bool manualReviewBusy;

    private readonly ListView issueList = new()
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
    private readonly ListView failedList = new()
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
    private readonly Button resolve = UiControls.StandardButton("標記已解決");
    private readonly Button approveManualReview = UiControls.StandardButton("確認送出作廢");
    private readonly Button cancelManualReview = UiControls.StandardButton("取消退回");
    private readonly Button deleteFailed = UiControls.StandardButton("刪除");
    private readonly Button deleteAllFailed = UiControls.StandardButton("全部刪除");

    public SyncIssuesForm(LocalRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        failedStore = new FailedInvoiceRecordStore(repository.DataDirectory);
        voidWorkflow = new EmployeeVoidWorkflowService(repository);
        accountKey = CurrentAccountKey();
        lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);

        Text = "上傳問題";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 450);
        ClientSize = new Size(840, 500);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.White;

        BuildLayout();
        ReloadAll();
        Shown += (_, _) => MarkVisibleIssuesRead();
    }

    private void BuildLayout()
    {
        ConfigureIssueList();
        ConfigureFailedList();

        var reload = UiControls.StandardButton("重新整理");
        reload.Click += (_, _) => ReloadAll();
        resolve.Enabled = false;
        resolve.Click += (_, _) => ResolveSelected();
        approveManualReview.Enabled = false;
        approveManualReview.Click += async (_, _) => await ApproveSelectedManualReviewAsync();
        cancelManualReview.Enabled = false;
        cancelManualReview.Click += (_, _) => CancelSelectedManualReview();
        issueList.SelectedIndexChanged += (_, _) => UpdateIssueButtons();

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

        var issueActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        issueActions.Controls.Add(resolve);
        issueActions.Controls.Add(approveManualReview);
        issueActions.Controls.Add(cancelManualReview);

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
            RowCount = 6,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(issueHeader, 0, 0);
        root.Controls.Add(issueList, 0, 1);
        root.Controls.Add(issueActions, 0, 2);
        root.Controls.Add(failedHeader, 0, 3);
        root.Controls.Add(failedList, 0, 4);
        root.Controls.Add(bottomActions, 0, 5);
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
                        "人工確認" => Color.FromArgb(190, 120, 0),
                        "已解決" => Color.FromArgb(0, 132, 72),
                        _ => Color.DimGray,
                    };
                    if (state == "人工確認") stateCell.Font = new Font(issueList.Font, FontStyle.Bold);
                    issueList.Items.Add(row);
                }
            }
            finally
            {
                issueList.EndUpdate();
            }

            var unresolved = issues.Count(issue => issue.ResolvedUtc is null);
            summary.Text = unresolved == 0 ? "目前沒有尚未解決的上傳問題" : $"尚未解決：{unresolved} 項";
            UpdateIssueButtons();
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
            ReloadIssues();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "更新上傳問題已讀狀態失敗：" + error.Message, "狀態更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ResolveSelected()
    {
        if (issueList.SelectedItems.Count != 1 || issueList.SelectedItems[0].Tag is not InvoiceSyncIssue issue) return;
        if (issue.ResolvedUtc is not null || IsManualReview(issue)) return;
        try
        {
            issueStore.Resolve(issue.Id, DateTimeOffset.Now);
            ReloadIssues();
            LayoutColumns();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "標記上傳問題失敗：" + error.Message, "操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ApproveSelectedManualReviewAsync()
    {
        var issue = SelectedManualReview();
        if (issue is null || manualReviewBusy) return;
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
            var result = await voidWorkflow.ApproveManualReviewAsync(
                issue,
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword);
            switch (result.Outcome)
            {
                case InvoiceVoidOutcome.Confirmed:
                case InvoiceVoidOutcome.AlreadyVoided:
                    MessageBox.Show(this, "光貿已確認發票作廢完成。", "作廢完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case InvoiceVoidOutcome.PendingConfirmation:
                    MessageBox.Show(
                        this,
                        "作廢已送出，但光貿尚未確認最終結果。\n\n系統已轉為待確認，不會盲目重送。",
                        "作廢結果待確認",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.Rejected:
                    MessageBox.Show(
                        this,
                        result.Message + "\n\n人工確認仍保留，尚未標記完成。",
                        "作廢未完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.RetryReady:
                    MessageBox.Show(
                        this,
                        "先前待確認狀態已解除，本次沒有自動重送。\n人工確認仍保留，可重新確認後再次送出。",
                        "請重新確認",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
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

    private void CancelSelectedManualReview()
    {
        var issue = SelectedManualReview();
        if (issue is null || manualReviewBusy) return;
        if (MessageBox.Show(
                this,
                "確定取消並退回這筆作廢申請？\n\n不會向光貿送出作廢，發票會維持「已開立」。",
                "取消退回",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository.Employees, "人工確認－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        SetManualReviewBusy(true);
        try
        {
            voidWorkflow.CancelManualReview(
                issue,
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword);
            MessageBox.Show(this, "已取消退回；未向光貿送出作廢。", "已取消",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private InvoiceSyncIssue? SelectedManualReview()
    {
        if (issueList.SelectedItems.Count != 1 || issueList.SelectedItems[0].Tag is not InvoiceSyncIssue issue)
            return null;
        return issue.ResolvedUtc is null && IsManualReview(issue) ? issue : null;
    }

    private void SetManualReviewBusy(bool busy)
    {
        manualReviewBusy = busy;
        issueList.Enabled = !busy;
        UpdateIssueButtons();
    }

    private void DeleteCheckedFailed()
    {
        var records = failedList.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<InvoiceRecord>()
            .ToArray();
        if (records.Length == 0) return;
        if (!ConfirmFailedDeletion(records.Length)) return;
        DeleteFailedRecords(records);
    }

    private void DeleteAllFailed()
    {
        var records = CurrentFailedRecords().ToArray();
        if (records.Length == 0) return;
        if (!ConfirmFailedDeletion(records.Length)) return;
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
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
        return repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 ||
                             string.Equals(record.SellerInvoice.Trim(), sellerInvoice, StringComparison.Ordinal))
            .Where(record => string.Equals(record.InvoiceState, InvoiceStates.Failed, StringComparison.Ordinal))
            .OrderByDescending(record => FullIssueTime(record), StringComparer.Ordinal)
            .ToArray();
    }

    private void UpdateIssueButtons()
    {
        var selected = issueList.SelectedItems.Count == 1 && issueList.SelectedItems[0].Tag is InvoiceSyncIssue issue
            ? issue
            : null;
        var unresolved = selected is { ResolvedUtc: null };
        var manual = unresolved && selected is not null && IsManualReview(selected);
        resolve.Enabled = !manualReviewBusy && unresolved && !manual;
        approveManualReview.Enabled = !manualReviewBusy && manual;
        cancelManualReview.Enabled = !manualReviewBusy && manual;
    }

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
        if (IsManualReview(issue)) return "人工確認";
        return lastRead is not null && issue.CreatedUtc <= lastRead.Value ? "已讀" : "未讀";
    }

    private static bool IsManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceVoidIssueTypes.ManualReview, StringComparison.Ordinal);

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
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
            var measured = TextRenderer.MeasureText(
                list.Columns[column].Text,
                list.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 18;
            if (checkboxFirstColumn && column == 0) measured += 22;

            foreach (ListViewItem item in list.Items)
            {
                if (column >= item.SubItems.Count) continue;
                var text = item.SubItems[column].Text;
                var valueWidth = TextRenderer.MeasureText(
                    text,
                    item.SubItems[column].Font ?? list.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 18;
                if (checkboxFirstColumn && column == 0) valueWidth += 22;
                measured = Math.Max(measured, valueWidth);
            }
            widths[column] = Math.Max(widths[column], measured);
        }

        var available = Math.Max(0, list.ClientSize.Width - 2);
        var total = widths.Sum();
        if (total < available)
            widths[flexibleColumn] += available - total;

        for (var column = 0; column < widths.Length; column++)
            list.Columns[column].Width = widths[column];
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

    private static string FullIssueTime(InvoiceRecord record)
    {
        if (record.InvoiceDate.Trim().Length != 0)
            return (record.InvoiceDate.Trim() + " " + record.InvoiceTime.Trim()).Trim();
        return record.SentAt.Trim();
    }

    private static string FriendlyFailureReason(string raw)
    {
        var message = raw.Trim();
        if (message.Length == 0) return "光貿未提供失敗原因";

        if (message.StartsWith("API code ", StringComparison.OrdinalIgnoreCase))
        {
            var colon = message.IndexOf(':');
            if (colon >= 0 && colon + 1 < message.Length)
                message = message[(colon + 1)..].Trim();
        }

        var field = FailureField(message);
        if (field.Length != 0)
        {
            if (ContainsAny(message, "required", "empty", "blank", "不可空", "不得空", "不能空"))
                return field + "不可空白";
            if (ContainsAny(message, "length", "長度"))
                return field + "長度不正確";
            if (ContainsAny(message, "duplicate", "already exists", "重複", "已存在"))
                return field + "重複";
            return field + "資料格式不正確";
        }

        if (message.Any(IsCjk)) return message;
        return "光貿拒絕此筆開立資料，請檢查發票內容";
    }

    private static string FailureField(string message)
    {
        var fields = new (string Technical, string Display)[]
        {
            ("BuyerIdentifier", "買受人統編"),
            ("BuyerName", "買受人名稱"),
            ("BuyerAddress", "買受人地址"),
            ("BuyerTelephoneNumber", "買受人電話"),
            ("BuyerEmailAddress", "買受人電子郵件"),
            ("OrderId", "訂單編號"),
            ("OrderID", "訂單編號"),
            ("SalesAmount", "銷售額"),
            ("TaxAmount", "稅額"),
            ("TotalAmount", "發票總額"),
            ("ProductItem", "商品明細"),
            ("Description", "商品名稱"),
            ("Quantity", "商品數量"),
            ("UnitPrice", "商品單價"),
            ("CarrierType", "載具類型"),
            ("CarrierId1", "載具號碼"),
            ("CarrierId2", "載具號碼"),
            ("NPOBAN", "捐贈碼"),
            ("MainRemark", "備註"),
            ("DetailVat", "明細含稅設定"),
        };
        foreach (var field in fields)
            if (message.Contains(field.Technical, StringComparison.OrdinalIgnoreCase)) return field.Display;
        return string.Empty;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u9fff';

    internal void VerifySmokeLayout()
    {
        if (Text != "上傳問題" || Math.Abs(Font.SizeInPoints - 10F) > 0.1F)
            throw new InvalidOperationException("上傳問題視窗標題或字級不正確");
        if (issueList.CheckBoxes || !failedList.CheckBoxes || issueList.Columns.Count != 6 || failedList.Columns.Count != 6)
            throw new InvalidOperationException("上傳問題上下清單結構不正確");
        if (!issueList.GridLines || !failedList.GridLines || !issueList.Scrollable || !failedList.Scrollable)
            throw new InvalidOperationException("上傳問題上下清單未保留直向格線或水平捲動能力");
        if (issueList.Columns[5].Text != "狀態")
            throw new InvalidOperationException("上傳問題狀態欄未位於最右側");
        if (deleteFailed.Text != "刪除")
            throw new InvalidOperationException("未勾選開立失敗紀錄時刪除按鈕不應顯示 (0)");
        if (!UiControls.HasLogicalSize(approveManualReview, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(cancelManualReview, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("人工確認按鈕未使用標準尺寸");
        if (FriendlyFailureReason("API code 3040122: BuyerIdentifier invalid") != "買受人統編資料格式不正確")
            throw new InvalidOperationException("開立失敗原因未轉為可理解中文");
        if (DisplayType(InvoiceVoidIssueTypes.ManualReview) != "紙本作廢確認")
            throw new InvalidOperationException("人工確認類型顯示不正確");
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
        _ => type,
    };
}
