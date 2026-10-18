using CYInvoice.Core;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// Manager-only presentation for exceptional Work Items, never an invoice ledger.
// This form cannot issue, void, retry or acknowledge an AMEGO provider action.
internal sealed class CloudWorkItemsForm : Form
{
    private readonly LocalRepository repository;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly CloudWorkItemClient client;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView entries = new() { Dock = DockStyle.Fill, View = View.Details,
        FullRowSelect = true, MultiSelect = false, GridLines = true, HideSelection = false };
    private readonly ComboBox filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
    private readonly Button refresh = UiControls.StandardButton("重新整理");
    private readonly TextBox itemIdLookup = new() { Width = 300, MaxLength = 100,
        PlaceholderText = "wi_ 開頭的工作項目 ID" };
    private readonly Button lookup = UiControls.StandardButton("依 ID 查回");
    private readonly Button nextPage = UiControls.StandardButton("下一頁");
    private readonly Button takeReview = UiControls.StandardButton("接手覆核");
    private readonly Button archive = UiControls.StandardButton("人工封存");
    private readonly Button close = UiControls.StandardButton("關閉");
    private CloudWorkItemCursor? cursor;
    private CloudWorkItemCursor? nextCursor;
    private bool busy;

    // Shared actual presentation path: the layout smoke uses this constructor
    // without loading settings, Device credentials, CYID, or making HTTP calls.
    private CloudWorkItemsForm()
    {
        repository = null!;
        client = null!;
        Text = "跨機待辦（非折讓）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 500);
        MinimumSize = new Size(740, 400);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.White;
        ShowIcon = false;
        BuildLayout();
        FormClosing += (_, _) => lifetime.Cancel();
        FormClosed += (_, _) => { http.Dispose(); lifetime.Dispose(); };
    }

    public CloudWorkItemsForm(LocalRepository repository) : this()
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        var setting = repository.Settings.LoadOrCreate();
        if (setting.CloudMode == CloudModes.LocalOnly ||
            setting.CloudIdentityProvider != "CYID" || !setting.CloudEmployeeAuthorityReady ||
            setting.CloudDeviceId.Length == 0 || setting.CloudWorkspaceId.Length == 0)
            throw new InvalidOperationException("CYID Cloud Device is required.");
        var token = repository.Settings.CloudDeviceToken(setting);
        client = new CloudWorkItemClient(http, new Uri(setting.CloudBaseUrl, UriKind.Absolute),
            token, setting.CloudWorkspaceId);
        Shown += async (_, _) => await LoadPageAsync(reset: true);
    }

    private void BuildLayout()
    {
        filter.Items.AddRange(["待處理與覆核中", "待處理", "覆核中", "已封存"]);
        filter.SelectedIndex = 0;
        filter.SelectedIndexChanged += (_, _) => { ClearView();
            status.Text = "請按重新整理載入。"; UpdateActions(); };
        entries.Columns.Add("建立時間", 155);
        entries.Columns.Add("種類", 110);
        entries.Columns.Add("上傳問題代碼", 160);
        entries.Columns.Add("人工狀態", 95);
        entries.Columns.Add("版次", 55);
        entries.Columns.Add("環境", 75);
        entries.Columns.Add("工作項目 ID", 245);
        entries.SelectedIndexChanged += (_, _) => UpdateActions();
        refresh.Click += async (_, _) => await LoadPageAsync(reset: true);
        lookup.Click += async (_, _) => await LookupByIdAsync();
        itemIdLookup.TextChanged += (_, _) => UpdateActions();
        nextPage.Click += async (_, _) => await LoadPageAsync(reset: false);
        takeReview.Click += async (_, _) => await TransitionAsync("review");
        archive.Click += async (_, _) => await TransitionAsync("closed");
        close.Click += (_, _) => Close();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 2 };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        filters.Controls.Add(new Label { Text = "狀態", Width = 48, Height = 30,
            TextAlign = ContentAlignment.MiddleLeft });
        filters.Controls.Add(filter);
        filters.Controls.Add(refresh);
        heading.Controls.Add(filters, 0, 0);
        var readback = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        readback.Controls.Add(new Label { Text = "ID 查回", Width = 75, Height = 30,
            TextAlign = ContentAlignment.MiddleLeft });
        readback.Controls.Add(itemIdLookup);
        readback.Controls.Add(lookup);
        heading.Controls.Add(readback, 0, 1);
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(entries, 0, 1);

        status.Text = "僅顯示例外工作。人工封存不代表光貿已作廢或上傳成功。";
        root.Controls.Add(status, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 5, 0, 0) };
        actions.Controls.Add(close);
        actions.Controls.Add(archive);
        actions.Controls.Add(takeReview);
        actions.Controls.Add(nextPage);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        UpdateActions();
    }

    // Never retain an actionable manager queue after a failed fresh CYID read,
    // uncertain mutation result or concurrency conflict.
    private void ClearView()
    {
        entries.Items.Clear();
        cursor = null;
        nextCursor = null;
    }

    private CloudWorkItemSummary? Selected => entries.SelectedItems.Count == 1
        ? entries.SelectedItems[0].Tag as CloudWorkItemSummary : null;

    private void UpdateActions()
    {
        refresh.Enabled = !busy;
        filter.Enabled = !busy;
        itemIdLookup.Enabled = !busy;
        lookup.Enabled = !busy && !string.IsNullOrWhiteSpace(itemIdLookup.Text);
        nextPage.Enabled = !busy && nextCursor is not null;
        takeReview.Enabled = !busy && Selected?.Status == "open";
        archive.Enabled = !busy && Selected?.Status == "review";
    }

    private string? SelectedStatus() => filter.SelectedIndex switch
    {
        1 => "open", 2 => "review", 3 => "closed", _ => null
    };

    private bool CollectCredentials(out string employeeNo, out string password)
    {
        using var login = new EmployeeAdminLoginForm(repository,
            "CYID 管理員當次驗證", administratorRequired: false, collectCredentialsOnly: true);
        // The actual CYID principal is always established by the Worker,
        // not by any local account/cache state.
        if (login.ShowDialog(this) != DialogResult.OK)
        {
            employeeNo = string.Empty; password = string.Empty; return false;
        }
        employeeNo = login.EnteredEmployeeNo;
        password = login.AuthenticatedPassword;
        return true;
    }

    // Safe readback of an ambiguous report/transition: GET only, never replay
    // a VOID/UPLOAD_ISSUE write or imply an official AMEGO provider result.
    private async Task LookupByIdAsync()
    {
        if (busy) return;
        var id = itemIdLookup.Text.Trim();
        if (id.Length < 10 || id.Length > 100 ||
            id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')))
        {
            status.Text = "請輸入正確的工作項目 ID。";
            return;
        }
        if (!CollectCredentials(out var employeeNo, out var password)) return;
        busy = true;
        ClearView(); // old list/selected actions must not be kept as fresh authority
        UpdateActions();
        try
        {
            var item = await client.GetAsync(id, employeeNo, password, lifetime.Token);
            if (IsDisposed) return;
            status.Text = item is null
                ? "查無此工作項目；請確認 ID 與工作空間。"
                : $"ID 查回：{(item.Status switch {
                    "open" => "待處理", "review" => "覆核中", _ => "已封存"
                })}，版次 {item.Revision}；非光貿官方狀態。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsDisposed)
                status.Text = "依 ID 查回失敗；請檢查 CYID 權限、裝置與網路，不會重送操作。";
        }
        finally
        {
            busy = false;
            if (!IsDisposed) UpdateActions();
        }
    }

    private async Task LoadPageAsync(bool reset)
    {
        if (busy || !reset && nextCursor is null) return;
        if (!CollectCredentials(out var employeeNo, out var password)) return;
        busy = true;
        UpdateActions();
        try
        {
            var target = reset ? null : nextCursor;
            var page = await client.ListAsync(employeeNo, password, 20, status: SelectedStatus(),
                cursor: target, cancellationToken: lifetime.Token);
            if (IsDisposed) return;
            entries.BeginUpdate();
            try
            {
                entries.Items.Clear();
                foreach (var item in page.Items)
                {
                    var row = new ListViewItem(item.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                    row.SubItems.Add(item.Kind == "VOID" ? "作廢覆核" : "上傳問題");
                    row.SubItems.Add(item.IssueCode ?? "—");
                    row.SubItems.Add(item.Status switch { "open" => "待處理", "review" => "覆核中",
                        _ => "已封存" });
                    row.SubItems.Add(item.Revision.ToString());
                    row.SubItems.Add(item.Environment == "production" ? "正式" : "測試");
                    row.SubItems.Add(item.ItemId);
                    row.Tag = item;
                    entries.Items.Add(row);
                }
                cursor = target;
                nextCursor = page.NextCursor;
                status.Text = $"本頁 {page.Items.Count} 筆；人工工作狀態僅供管理追蹤，非官方發票狀態。";
            }
            finally { entries.EndUpdate(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsDisposed) return;
            ClearView();
            status.Text = error is CloudApiException api && api.Code == "WORK_ITEMS_NOT_READY"
                ? "跨機待辦尚未啟用，等待最後階段的本機 Cloudflare MCP 升級。"
                : "查詢失敗，請檢查 CYID 權限、裝置狀態及連線；未變更工作項目。";
        }
        finally
        {
            busy = false;
            if (!IsDisposed) UpdateActions();
        }
    }

    private async Task TransitionAsync(string to)
    {
        var chosen = Selected;
        if (busy || chosen is null ||
            (to == "review" && chosen.Status != "open") ||
            (to == "closed" && chosen.Status != "review")) return;
        var question = to == "review"
            ? "接手此工作項目，將狀態改為「覆核中」？\n此操作不會執行光貿作廢或重新上傳。"
            : "將此工作項目「人工封存」？\n這不代表發票已正式作廢或上傳成功。";
        if (MessageBox.Show(this, question, "確認人工處理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!CollectCredentials(out var employeeNo, out var password)) return;
        busy = true; UpdateActions();
        try
        {
            // GET latest revision with fresh CYID authority; never rely on a
            // possibly stale row from the manager's local list.
            var current = await client.GetAsync(chosen.ItemId, employeeNo, password, lifetime.Token);
            if (current is null || current.Revision != chosen.Revision || current.Status != chosen.Status)
            {
                itemIdLookup.Text = chosen.ItemId;
                ClearView();
                status.Text = "工作項目已變更，請使用「依 ID 查回」核對最新狀態。";
                return;
            }
            string? resolutionCode = null;
            if (to == "closed" && current.Kind == "UPLOAD_ISSUE")
            {
                resolutionCode = AskResolutionCode();
                if (resolutionCode is null) return;
            }
            await client.TransitionManualAsync(current, to, employeeNo, password,
                resolutionCode, lifetime.Token);
            status.Text = "人工狀態已更新；請重新整理最新清單。";
            ClearView();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            // A lost D1 reply may follow a successful commit. Preserve the
            // exact ID for the read-only lookup, not a second mutation.
            if (IsDisposed) return;
            itemIdLookup.Text = chosen.ItemId;
            ClearView();
            status.Text = "結果不明或權限變更；已保留原 ID，按「依 ID 查回」核對，不會自動重送。";
        }
        finally
        {
            busy = false;
            if (!IsDisposed) UpdateActions();
        }
    }

    private string? AskResolutionCode()
    {
        using var dialog = new Form { Text = "上傳問題封存原因", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 130), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowIcon = false,
            Font = new Font("Microsoft JhengHei UI", 10F) };
        var reasons = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(10) };
        reasons.Items.AddRange(["已於本機解決", "已確認官方狀態", "重複報告",
            "不需處理", "仍需追蹤"]);
        reasons.SelectedIndex = 4;
        var ok = UiControls.StandardButton("確定");
        ok.DialogResult = DialogResult.OK;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42,
            FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.Add(ok);
        dialog.Controls.Add(new Label { Dock = DockStyle.Top, Height = 40,
            Text = "請選擇人工封存原因（不改變官方狀態）", TextAlign = ContentAlignment.MiddleLeft });
        dialog.Controls.Add(reasons);
        dialog.Controls.Add(panel);
        dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        return reasons.SelectedIndex switch
        {
            0 => "resolved_locally", 1 => "checked_official", 2 => "duplicate_report",
            3 => "not_actionable", _ => "needs_followup"
        };
    }

    internal static void VerifySmokeLayout(Form owner)
    {
        // Exercise the SAME controls and layout as production, not an empty
        // placeholder Form. No repository settings, credentials, or network.
        using var sample = new CloudWorkItemsForm();
        sample.Show(owner);
        sample.PerformLayout();
        if (sample.ClientSize.Width != 920 || sample.ClientSize.Height != 500 ||
            sample.Controls.Count != 1 ||
            sample.Controls[0] is not TableLayoutPanel root || root.RowCount != 4 ||
            root.GetControlFromPosition(0, 0) is not TableLayoutPanel heading ||
            heading.RowCount != 2 ||
            heading.GetControlFromPosition(0, 1) is not FlowLayoutPanel ||
            root.GetControlFromPosition(0, 1) != sample.entries ||
            root.GetControlFromPosition(0, 2) != sample.status ||
            root.GetControlFromPosition(0, 3) is not FlowLayoutPanel ||
            sample.entries.Columns.Count != 7 ||
            sample.filter.Items.Count != 4 ||
            sample.entries.Width <= 0 || sample.entries.Height <= 0 ||
            !sample.refresh.Enabled || sample.nextPage.Enabled || sample.lookup.Enabled ||
            sample.itemIdLookup.Width <= 0 ||
            sample.takeReview.Enabled || sample.archive.Enabled)
            throw new InvalidOperationException("跨機待辦控制項或版面配置異常");
        sample.itemIdLookup.Text = "wi_synthetic_readback";
        if (!sample.lookup.Enabled)
            throw new InvalidOperationException("跨機待辦 ID 查回按鈕應隨有效輸入啟用");
        sample.itemIdLookup.Clear();
        if (sample.lookup.Enabled)
            throw new InvalidOperationException("跨機待辦空白 ID 不得送出查回");
        sample.Close();
    }
}
