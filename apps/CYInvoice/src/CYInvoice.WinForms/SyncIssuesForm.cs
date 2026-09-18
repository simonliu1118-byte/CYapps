using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SyncIssuesForm : Form
{
    private readonly LocalRepository repository;
    private readonly InvoiceSyncIssueStore store;
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };
    private readonly Label status = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Anchor = AnchorStyles.Left,
    };

    public SyncIssuesForm(LocalRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        store = new InvoiceSyncIssueStore(repository.DataDirectory);
        Text = "同步問題";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(780, 420);
        ClientSize = new Size(940, 520);
        Font = new Font("Microsoft JhengHei UI", 11F);
        BackColor = Color.White;

        BuildLayout();
        ReloadIssues();
    }

    private void BuildLayout()
    {
        list.Columns.Add("時間", 150, HorizontalAlignment.Left);
        list.Columns.Add("類型", 170, HorizontalAlignment.Left);
        list.Columns.Add("發票號碼", 115, HorizontalAlignment.Left);
        list.Columns.Add("訂單編號", 170, HorizontalAlignment.Left);
        list.Columns.Add("內容", 320, HorizontalAlignment.Left);

        var resolve = UiControls.StandardButton("標記完成");
        resolve.Click += (_, _) => ResolveSelected();
        var delete = UiControls.StandardButton("刪除");
        delete.Click += (_, _) => DeleteSelected();
        var reload = UiControls.StandardButton("重新整理");
        reload.Click += (_, _) => ReloadIssues();
        var close = UiControls.StandardButton("關閉");
        close.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.Controls.Add(resolve);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(reload);
        buttons.Controls.Add(close);
        buttons.Controls.Add(status);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.Controls.Add(list, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
    }

    private void ReloadIssues()
    {
        try
        {
            var issues = store.Unresolved(CurrentAccountKey());
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var issue in issues)
                {
                    var row = new ListViewItem(issue.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"));
                    row.SubItems.Add(DisplayType(issue.IssueType));
                    row.SubItems.Add(issue.InvoiceNumber);
                    row.SubItems.Add(issue.OrderId);
                    row.SubItems.Add(issue.Message);
                    row.Tag = issue;
                    list.Items.Add(row);
                }
            }
            finally
            {
                list.EndUpdate();
            }
            status.Text = issues.Count == 0 ? "目前沒有未解決的同步問題" : $"未解決：{issues.Count} 項";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "讀取同步問題失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResolveSelected()
    {
        if (SelectedIssue() is not { } issue) return;
        try
        {
            store.Resolve(issue.Id, DateTimeOffset.Now);
            ReloadIssues();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "標記同步問題失敗：" + error.Message, "操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedIssue() is not { } issue) return;
        var answer = MessageBox.Show(
            this,
            "確定要刪除這筆同步問題紀錄嗎？\n\n刪除只會移除問題紀錄，不會刪除發票。",
            "刪除同步問題",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        try
        {
            store.Delete(issue.Id);
            ReloadIssues();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "刪除同步問題失敗：" + error.Message, "操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private InvoiceSyncIssue? SelectedIssue()
    {
        if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is InvoiceSyncIssue issue) return issue;
        MessageBox.Show(this, "請先選取一筆同步問題。", "尚未選取", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
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
