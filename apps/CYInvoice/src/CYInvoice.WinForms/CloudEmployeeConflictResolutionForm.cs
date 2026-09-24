using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudEmployeeConflictResolutionForm : Form
{
    private readonly LocalRepository repository;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView conflicts = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        GridLines = true,
    };
    private readonly ListView candidates = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        GridLines = true,
    };
    private readonly Label summary = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Button resolve = UiControls.StandardButton("採用選取雲端帳號");
    private readonly Button refresh = UiControls.StandardButton("重新整理");
    private readonly Button close = UiControls.StandardButton("關閉");
    private bool busy;
    private bool resourcesDisposed;

    public CloudEmployeeConflictResolutionForm(LocalRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        if (!repository.UsesCloudEmployeeAuthority())
            throw new InvalidOperationException("帳號身分人工確認只能在已完成 Cloud Employee 切換的裝置執行。");

        Text = "帳號身分待確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(860, 520);
        MinimumSize = new Size(800, 470);
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += async (_, _) => await ReloadWithAuthenticationAsync();
    }

    public int RemainingCount => conflicts.Items.Count;

    private void BuildLayout()
    {
        conflicts.Columns.Add("來源裝置", 150, HorizontalAlignment.Left);
        conflicts.Columns.Add("本機員編", 82, HorizontalAlignment.Center);
        conflicts.Columns.Add("本機姓名", 120, HorizontalAlignment.Left);
        conflicts.Columns.Add("本機 Email", 240, HorizontalAlignment.Left);
        conflicts.Columns.Add("衝突原因", 210, HorizontalAlignment.Left);
        conflicts.SelectedIndexChanged += (_, _) => FillCandidates();

        candidates.Columns.Add("命中來源", 100, HorizontalAlignment.Center);
        candidates.Columns.Add("雲端員編", 82, HorizontalAlignment.Center);
        candidates.Columns.Add("雲端姓名", 125, HorizontalAlignment.Left);
        candidates.Columns.Add("雲端 Email", 250, HorizontalAlignment.Left);
        candidates.Columns.Add("雲端權限", 110, HorizontalAlignment.Center);
        candidates.SelectedIndexChanged += (_, _) => UpdateActions();
        candidates.DoubleClick += async (_, _) => await ResolveSelectedAsync();

        resolve.Click += async (_, _) => await ResolveSelectedAsync();
        refresh.Click += async (_, _) => await ReloadWithAuthenticationAsync();
        close.DialogResult = DialogResult.Cancel;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        resolve.Width = 150;
        actions.Controls.Add(close);
        actions.Controls.Add(resolve);
        actions.Controls.Add(refresh);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12, 10, 12, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(conflicts, 0, 1);
        root.Controls.Add(new Label
        {
            Text = "請選擇此本機帳號實際對應的既有雲端帳號：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 2);
        root.Controls.Add(candidates, 0, 3);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
        UpdateActions();
    }

    private async Task ReloadWithAuthenticationAsync()
    {
        if (busy) return;
        var auth = AuthenticateSuperAdmin("帳號身分待確認－超管驗證");
        if (auth is null) return;
        await RunBusyAsync(async () =>
        {
            var client = CreateClient();
            var items = await client.ListAsync(auth.Value.EmployeeNo, auth.Value.Password, lifetime.Token);
            FillConflicts(items);
        });
    }

    private void FillConflicts(IReadOnlyList<CloudEmployeeTransitionConflict> items)
    {
        conflicts.BeginUpdate();
        try
        {
            conflicts.Items.Clear();
            foreach (var item in items)
            {
                var row = new ListViewItem(item.SourceDeviceName) { Tag = item };
                row.SubItems.Add(item.LocalEmployeeNo);
                row.SubItems.Add(item.LocalName);
                row.SubItems.Add(item.LocalEmail);
                row.SubItems.Add(ConflictText(item.MatchKind));
                conflicts.Items.Add(row);
            }
        }
        finally
        {
            conflicts.EndUpdate();
        }

        candidates.Items.Clear();
        summary.Text = items.Count == 0
            ? "目前沒有待確認的帳號身分。"
            : $"待確認 {items.Count} 筆。只有目前 Workspace SUPER_ADMIN 可以指定正確的既有雲端帳號。";
        if (conflicts.Items.Count != 0) conflicts.Items[0].Selected = true;
        UpdateActions();
    }

    private void FillCandidates()
    {
        candidates.BeginUpdate();
        try
        {
            candidates.Items.Clear();
            if (SelectedConflict() is not { } conflict) return;
            foreach (var employee in conflict.Candidates)
            {
                var source = CandidateSource(conflict, employee.EmployeeId);
                var row = new ListViewItem(source) { Tag = employee };
                row.SubItems.Add(employee.EmployeeNo);
                row.SubItems.Add(employee.Name);
                row.SubItems.Add(employee.Email);
                row.SubItems.Add(RoleText(employee.Role));
                candidates.Items.Add(row);
            }
            if (candidates.Items.Count == 1) candidates.Items[0].Selected = true;
        }
        finally
        {
            candidates.EndUpdate();
        }
        UpdateActions();
    }

    private async Task ResolveSelectedAsync()
    {
        if (busy || SelectedConflict() is not { } conflict || SelectedCandidate() is not { } target) return;
        var message =
            $"本機帳號\n{conflict.LocalEmployeeNo}  {conflict.LocalName}\n{conflict.LocalEmail}\n\n" +
            $"將改用既有雲端帳號\n{target.EmployeeNo}  {target.Name}\n{target.Email}\n{RoleText(target.Role)}\n\n" +
            "確認後，此來源裝置完成 Cloud 切換時會直接採用這筆雲端帳號資料。確定？";
        if (MessageBox.Show(this, message, "確認帳號身分", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var auth = AuthenticateSuperAdmin("確認帳號身分－超管驗證");
        if (auth is null) return;
        await RunBusyAsync(async () =>
        {
            var client = CreateClient();
            await client.ResolveAsync(
                auth.Value.EmployeeNo,
                auth.Value.Password,
                conflict.TransitionItemId,
                target.EmployeeId,
                lifetime.Token);

            // Do not reuse the password after the action. Re-authenticate for a new
            // server list read so every sensitive review action remains scoped.
            conflicts.Items.Remove(conflicts.SelectedItems[0]);
            candidates.Items.Clear();
            summary.Text = conflicts.Items.Count == 0
                ? "目前沒有待確認的帳號身分。"
                : $"待確認 {conflicts.Items.Count} 筆。其餘項目仍需逐筆確認。";
            UpdateActions();
        });
    }

    private (string EmployeeNo, string Password)? AuthenticateSuperAdmin(string title)
    {
        using var login = new EmployeeAdminLoginForm(repository, title);
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return null;
        if (login.AuthenticatedEmployee.Role != EmployeeRoles.SuperAdmin)
        {
            MessageBox.Show(this, "此操作僅限目前 Workspace 超級管理員。", "權限不足",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return (login.AuthenticatedEmployee.EmployeeNo, login.AuthenticatedPassword);
    }

    private CloudEmployeeTransitionConflictClient CreateClient()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (!repository.UsesCloudEmployeeAuthority())
            throw new InvalidOperationException("這台電腦尚未完成 Cloud Employee 權限切換。");
        var token = repository.Settings.CloudDeviceToken(settings);
        if (token.Length == 0) throw new InvalidOperationException("本機缺少 Cloud Device Token。");
        return new CloudEmployeeTransitionConflictClient(
            httpClient,
            new Uri(settings.CloudBaseUrl, UriKind.Absolute),
            token);
    }

    private CloudEmployeeTransitionConflict? SelectedConflict() =>
        conflicts.SelectedItems.Count == 1 ? conflicts.SelectedItems[0].Tag as CloudEmployeeTransitionConflict : null;

    private CloudEmployeeTransitionIdentity? SelectedCandidate() =>
        candidates.SelectedItems.Count == 1 ? candidates.SelectedItems[0].Tag as CloudEmployeeTransitionIdentity : null;

    private void UpdateActions()
    {
        resolve.Enabled = !busy && SelectedConflict() is not null && SelectedCandidate() is not null;
        refresh.Enabled = !busy;
        close.Enabled = !busy;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        UpdateActions();
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this, FriendlyMessage(error), "帳號身分待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
                UpdateActions();
            }
        }
    }

    private static string CandidateSource(CloudEmployeeTransitionConflict conflict, string employeeId)
    {
        var byNo = conflict.EmployeeNoMatch?.EmployeeId == employeeId;
        var byEmail = conflict.EmailMatch?.EmployeeId == employeeId;
        return (byNo, byEmail) switch
        {
            (true, true) => "員編 + Email",
            (true, false) => "員編",
            (false, true) => "Email",
            _ => "候選帳號",
        };
    }

    private static string ConflictText(string kind) => kind switch
    {
        "employee_no_only" => "員編已存在／Email 不同",
        "email_only" => "Email 已存在／員編不同",
        "split" => "員編與 Email 指向不同帳號",
        _ => "帳號身分衝突",
    };

    private static string RoleText(string role) => role switch
    {
        EmployeeRoles.SuperAdmin => "超級管理員",
        EmployeeRoles.Admin => "管理員",
        EmployeeRoles.Employee => "一般員工",
        _ => role,
    };

    private static string FriendlyMessage(Exception error)
    {
        if (error is not CloudApiException api) return error.Message;
        return api.Code switch
        {
            "SUPER_ADMIN_REQUIRED" => "超級管理員帳密驗證失敗，或目前帳號已不是 Workspace 超級管理員。",
            "TRANSITION_CONFLICT_NOT_FOUND" => "這筆待確認帳號已由其他操作完成處理，請重新整理。",
            "TRANSITION_TARGET_NOT_CANDIDATE" => "選取的雲端帳號已不是這筆衝突的候選帳號，請重新整理。",
            "TRANSITION_TARGET_NOT_READY" => "選取的雲端帳號尚未完成 Email 或登入憑證設定，暫時不能採用。",
            _ => $"Cloud API 錯誤：{api.Code}\n{api.Message}",
        };
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "帳號身分待確認" || ShowIcon || AcceptButton is not null || CancelButton != close)
            throw new InvalidOperationException("帳號身分待確認視窗基本屬性不正確");
        if (conflicts.Columns.Count != 5 || candidates.Columns.Count != 5 || resolve.Text != "採用選取雲端帳號")
            throw new InvalidOperationException("帳號身分待確認視窗欄位或動作不正確");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            httpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
