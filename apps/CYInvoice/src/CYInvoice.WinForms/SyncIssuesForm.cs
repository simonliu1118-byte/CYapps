using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SyncIssuesForm : Form
{
    internal const string ReadStateScope = "upload-issues-read";

    private readonly LocalRepository repository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly FailedInvoiceRecordStore failedStore;
    private readonly string accountKey;
    private DateTimeOffset? lastRead;

    private readonly ListView issueList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        BorderStyle = BorderStyle.FixedSingle,
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
    };
    private readonly Label summary = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Anchor = AnchorStyles.Left,
    };
    private readonly Button resolve = UiControls.StandardButton("標記已解決");
    private readonly Button deleteFailed = UiControls.StandardButton("刪除 (0)");
    private readonly Button deleteAllFailed = UiControls.StandardButton("全部刪除");

    public SyncIssuesForm(LocalRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        failedStore = new FailedInvoiceRecordStore(repository.DataDirectory);
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
        issueList.SelectedIndexChanged += (_, _) => UpdateResolveButton();

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
        issueList.Columns.Add("時間", 130, HorizontalAlignment.Left);
        issueList.Columns.Add("類型", 145, HorizontalAlignment.Left);
        issueList.Columns.Add("發票號碼", 105, HorizontalAlignment.Left);
        issueList.Columns.Add("訂單編號", 135, HorizontalAlignment.Left);
        issueList.Columns.Add("內容", 220, HorizontalAlignment.Left);
        issueList.Columns.Add("狀態", 70, HorizontalAlignment.Center);
    }

    private void ConfigureFailedList()
    {
        failedList.Columns.Add("時間", 135, HorizontalAlignment.Left);
        failedList.Columns.Add("來源", 90, HorizontalAlignment.Left);
        failedList.Columns.Add("訂單編號", 145, HorizontalAlignment.Left);
        failedList.Columns.Add("買受人", 130, HorizontalAlignment.Left);
        failedList.Columns.Add("金額", 80, HorizontalAlignment.Right);
        failedList.Columns.Add("失敗原因", 220, HorizontalAlignment.Left);
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
                        "已解決" => Color.FromArgb(0, 132, 72),
                        _ => Color.DimGray,
                    };
                    issueList.Items.Add(row);
                }
            }
            finally
            {
                issueList.EndUpdate();
            }

            var unresolved = issues.Count(issue => issue.ResolvedUtc is null);
            summary.Text = unresolved == 0 ? "目前沒有尚未解決的上傳問題" : $"尚未解決：{unresolved} 項";
            UpdateResolveButton();
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
                    row.SubItems.Add(record.ErrorMessage);
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
        if (issue.ResolvedUtc is not null) return;
        try
        {
            issueStore.Resolve(issue.Id, DateTimeOffset.Now);
            ReloadIssues();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "標記上傳問題失敗：" + error.Message, "操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private void UpdateResolveButton()
    {
        resolve.Enabled = issueList.SelectedItems.Count == 1 &&
                          issueList.SelectedItems[0].Tag is InvoiceSyncIssue { ResolvedUtc: null };
    }

    private void UpdateFailedButtons()
    {
        if (IsDisposed) return;
        var count = failedList.CheckedItems.Count;
        deleteFailed.Text = $"刪除 ({count})";
        deleteFailed.Enabled = count > 0;
        deleteAllFailed.Enabled = failedList.Items.Count > 0;
    }

    private string IssueState(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null) return "已解決";
        return lastRead is not null && issue.CreatedUtc <= lastRead.Value ? "已讀" : "未讀";
    }

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
        if (issueList.ClientSize.Width > 100)
        {
            var fixedWidth = 130 + 145 + 105 + 135 + 70;
            issueList.Columns[4].Width = Math.Max(150, issueList.ClientSize.Width - fixedWidth - 8);
        }
        if (failedList.ClientSize.Width > 100)
        {
            var fixedWidth = 135 + 90 + 145 + 130 + 80;
            failedList.Columns[5].Width = Math.Max(150, failedList.ClientSize.Width - fixedWidth - 8);
        }
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

    internal void VerifySmokeLayout()
    {
        if (Text != "上傳問題" || Math.Abs(Font.SizeInPoints - 10F) > 0.1F)
            throw new InvalidOperationException("上傳問題視窗標題或字級不正確");
        if (issueList.CheckBoxes || !failedList.CheckBoxes || issueList.Columns.Count != 6 || failedList.Columns.Count != 6)
            throw new InvalidOperationException("上傳問題上下清單結構不正確");
        if (issueList.Columns[5].Text != "狀態")
            throw new InvalidOperationException("上傳問題狀態欄未位於最右側");
        if (deleteFailed.Text != "刪除 (0)")
            throw new InvalidOperationException("開立失敗刪除按鈕未顯示勾選數量");
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
        _ => type,
    };
}
