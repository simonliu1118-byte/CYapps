using CYInvoice.Core;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// Manager-only presentation for exceptional Work Items, never an invoice ledger.
// This form cannot issue, void, retry or acknowledge an AMEGO provider action.
internal sealed class CloudWorkItemsControl : UserControl
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
    private readonly Button archive = UiControls.StandardButton("人工封存");
    private readonly Button close = UiControls.StandardButton("關閉");
    private CloudWorkItemCursor? cursor;
    private CloudWorkItemCursor? nextCursor;
    private bool busy;
    private bool initialLoad;

    // Shared actual presentation path: the layout smoke uses this constructor
    // without loading settings, Device credentials, CYID, or making HTTP calls.
    private CloudWorkItemsControl()
    {
        repository = null!;
        client = null!;

        ClientSize = new Size(920, 500);
        MinimumSize = new Size(740, 400);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.White;
        BuildLayout();

    }

    public CloudWorkItemsControl(LocalRepository repository) : this()
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
        VisibleChanged += async (_,_) =>
        {
            if (!Visible || initialLoad) return;
            initialLoad = true;
            await LoadPageAsync(reset: true);
        };

    }

    private void BuildLayout()
    {
        filter.Items.AddRange(["待處理與覆核中", "待處理", "覆核中", "已封存"]);
        filter.SelectedIndex = 0;
        filter.SelectedIndexChanged += (_, _) => { ClearView();
            status.Text = "請按重新整理載入。"; UpdateActions(); };
        entries.Columns.Add("建立時間", 120);
        entries.Columns.Add("種類", 80);
        entries.Columns.Add("上傳問題代碼", 110);
        entries.Columns.Add("人工狀態", 75);
        entries.Columns.Add("版次", 48);
        entries.Columns.Add("環境", 52);
        entries.Columns.Add("工作項目 ID", 190);
        entries.Resize += (_, _) => FitColumnsToViewport();
        entries.SelectedIndexChanged += (_, _) => UpdateActions();
        refresh.Click += async (_, _) => await LoadPageAsync(reset: true);
        lookup.Click += async (_, _) => await LookupByIdAsync();
        itemIdLookup.TextChanged += (_, _) => UpdateActions();
        nextPage.Click += async (_, _) => await LoadPageAsync(reset: false);
        archive.Click += async (_, _) => await ArchiveAsync();
        close.Click += (_, _) => FindForm()?.Close();

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
        actions.Controls.Add(nextPage);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        UpdateActions();
    }

    // Seven fixed legacy widths totaled 895px, which overflowed the 740px
    // minimum-size window even before allowing for the vertical scrollbar.
    // Share the actual viewport between columns, reserving an ID area,
    // without affecting Work Item state, selection or CYID authorization.
    private void FitColumnsToViewport()
    {
        if (entries.Columns.Count != 7 || entries.ClientSize.Width <= 0) return;
        const int scrollbarReserve = 4;
        var available = entries.ClientSize.Width -
            SystemInformation.VerticalScrollBarWidth - scrollbarReserve;
        if (available <= 0) return;
        // Fixed-point proportions preserve consistent density at 100/125/150% DPI.
        ReadOnlySpan<int> widthsPerThousand = [178, 112, 158, 111, 69, 76];
        var used = 0;
        for (var index = 0; index < widthsPerThousand.Length; index++)
        {
            var width = available * widthsPerThousand[index] / 1000;
            entries.Columns[index].Width = width;
            used += width;
        }
        entries.Columns[6].Width = available - used;
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
        archive.Enabled = !busy && Selected?.Status is "open" or "review";
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

    private async Task LoadPageAsync(bool reset, string? employeeNo = null, string? password = null)
    {
        if (busy || !reset && nextCursor is null) return;
        if ((employeeNo is null || password is null) && !CollectCredentials(out employeeNo, out password)) return;
        var selectedId = Selected?.ItemId;
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
                    if (item.ItemId == selectedId) row.Selected = true;
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
                ? "一般待辦尚未啟用，等待最後階段的本機 Cloudflare MCP 升級。"
                : "查詢失敗，請檢查 CYID 權限、裝置狀態及連線；未變更工作項目。";
        }
        finally
        {
            busy = false;
            if (!IsDisposed) UpdateActions();
        }
    }

    private async Task ArchiveAsync()
    {
        var chosen = Selected;
        if (busy || chosen is null || chosen.Status is not ("open" or "review")) return;
        if (!CollectCredentials(out var employeeNo, out var password)) return;
        busy = true; UpdateActions();
        string? outcome = null;
        try
        {
            var current = await client.GetAsync(chosen.ItemId, employeeNo, password, lifetime.Token);
            if (current is null || current.Revision != chosen.Revision || current.Status != chosen.Status)
            {
                outcome = "工作項目已變更，已自動重新整理，未執行封存。";
                return;
            }
            var reason = current.Kind == "UPLOAD_ISSUE" ? AskResolutionCode() : null;
            if (current.Kind == "UPLOAD_ISSUE" && reason is null) return;
            if (MessageBox.Show(this, "確認人工處置並封存這筆回報？\n這不代表原發票已作廢或上傳成功。",
                    "確認人工處置", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            // Existing revision/audit transitions remain internal to one user action.
            // Credentials live only within this action; each request revalidates CYID.
            if (current.Status == "open")
            {
                await client.TransitionManualAsync(current, "review", employeeNo, password, cancellationToken: lifetime.Token);
                current = current with { Status = "review", Revision = current.Revision + 1 };
            }
            await client.TransitionManualAsync(current, "closed", employeeNo, password, reason, lifetime.Token);
            outcome = "已人工封存；原發票問題仍以本機與官方查證結果為準。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsDisposed) return;
            itemIdLookup.Text = chosen.ItemId;
            try
            {
                var latest = await client.GetAsync(chosen.ItemId, employeeNo, password, lifetime.Token);
                outcome = latest is null ? "查無原工作項目，已保留 ID。" :
                    $"已自動查回原項目：{(latest.Status == "closed" ? "已人工封存" : "尚未封存")}；沒有重送操作。";
            }
            catch { outcome = "結果與查回均未確認；已保留原 ID，可稍後查詢。"; }
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                await LoadPageAsync(true, employeeNo, password);
                if (!IsDisposed && outcome is not null) status.Text = outcome;
            }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { lifetime.Cancel(); http.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    internal static void VerifySmokeLayout(Form owner)
    {
        // Exercise the SAME controls and layout as production, not an empty
        // placeholder Form. No repository settings, credentials, or network.
        using var sample = new CloudWorkItemsControl();
        using var host = new Form { ClientSize = sample.ClientSize, ShowIcon = false };
        sample.Dock = DockStyle.Fill; host.Controls.Add(sample); host.Show(owner);
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
            sample.archive.Enabled)
            throw new InvalidOperationException("一般待辦控制項或版面配置異常");
        static void AssertViewportColumns(CloudWorkItemsControl form)
        {
            form.FitColumnsToViewport();
            var used = form.entries.Columns.Cast<ColumnHeader>().Sum(column => column.Width);
            var room = form.entries.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (room <= 0 || used > room || form.entries.Columns[6].Width < 160)
                throw new InvalidOperationException(
                    "一般待辦欄寬超過視窗可用寬度，或工作項目 ID 欄過窄");
        }
        AssertViewportColumns(sample);
        host.ClientSize = new Size(740, 400);
        sample.PerformLayout();
        AssertViewportColumns(sample);
        host.ClientSize = new Size(920, 500);
        sample.PerformLayout();
        AssertViewportColumns(sample);
        sample.itemIdLookup.Text = "wi_synthetic_readback";
        if (!sample.lookup.Enabled)
            throw new InvalidOperationException("一般待辦 ID 查回按鈕應隨有效輸入啟用");
        sample.itemIdLookup.Clear();
        if (sample.lookup.Enabled)
            throw new InvalidOperationException("一般待辦空白 ID 不得送出查回");
        host.Close();
    }
}
