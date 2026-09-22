using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudEmployeeTransitionForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView list = new()
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
        AutoEllipsis = false,
    };
    private readonly Button refresh = UiControls.StandardButton("重新檢查");
    private readonly Button process = UiControls.StandardButton("處理可確認帳號");
    private readonly Button cutover = UiControls.StandardButton("完成雲端切換");
    private readonly Button close = UiControls.StandardButton("關閉");
    private CloudEmployeeTransitionInspection? inspection;
    private CloudEmployeeAuthorityStatus? authority;
    private bool busy;
    private bool resourcesDisposed;

    public CloudEmployeeTransitionForm(LocalRepository repository, Settings settings)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (settings.CloudMode != CloudModes.CloudTransition && !settings.CloudEmployeeAuthorityReady)
            throw new InvalidOperationException("目前沒有進行中的 Cloud Employee 帳號轉換。");

        Text = "雲端帳號轉換";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(780, 430);
        MinimumSize = new Size(720, 390);
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += async (_, _) => await RefreshAsync();
    }

    public bool AuthorityReady { get; private set; }

    private void BuildLayout()
    {
        list.Columns.Add("員工編號", 88, HorizontalAlignment.Center);
        list.Columns.Add("姓名", 135, HorizontalAlignment.Left);
        list.Columns.Add("原本權限", 100, HorizontalAlignment.Center);
        list.Columns.Add("雲端權限", 100, HorizontalAlignment.Center);
        list.Columns.Add("處理狀態", 210, HorizontalAlignment.Left);
        list.Columns.Add("Email", 190, HorizontalAlignment.Left);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        close.DialogResult = DialogResult.Cancel;
        refresh.Click += async (_, _) => await RefreshAsync();
        process.Click += async (_, _) => await ProcessResolvableAsync();
        cutover.Click += async (_, _) => await CutoverAsync();
        actions.Controls.Add(close);
        actions.Controls.Add(cutover);
        actions.Controls.Add(process);
        actions.Controls.Add(refresh);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 10, 12, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(list, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
        UpdateActions();
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            var (transitionClient, _, authorityClient) = CreateClients();
            authority = await authorityClient.GetStatusAsync(lifetime.Token);
            ValidateAuthorityIdentity(authority);

            if (string.Equals(authority.State, "cloud", StringComparison.Ordinal))
            {
                await FinalizeLocalCloudAuthorityAsync(authorityClient);
                ShowCompletedState();
                return;
            }

            var localEmployees = repository.Employees.LoadAll();
            if (localEmployees.Count == 0)
                throw new InvalidOperationException("本機沒有可轉換的員工帳號。");

            inspection = await transitionClient.InspectAsync(localEmployees, lifetime.Token);
            if (!string.Equals(inspection.DeviceId, settings.CloudDeviceId, StringComparison.Ordinal))
                throw new InvalidDataException("Cloud 帳號轉換回應的 Device ID 與本機不一致。");

            authority = await authorityClient.GetStatusAsync(lifetime.Token);
            ValidateAuthorityIdentity(authority);
            FillInspection();
        });
    }

    private async Task ProcessResolvableAsync()
    {
        if (inspection is null) return;
        await RunBusyAsync(async () =>
        {
            var (_, actionClient, _) = CreateClients();
            var credentialStore = new LocalEmployeeCredentialSnapshotStore(repository.DataDirectory);

            foreach (var item in inspection.Items)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                switch (item.State)
                {
                    case "ready":
                        continue;

                    case "bootstrap_owner_pending":
                    {
                        var verifier = credentialStore.GetRequiredVerifier(item.LocalEmployeeNo);
                        await actionClient.CompleteBootstrapOwnerAsync(
                            item.LocalEmployeeNo,
                            inspection.SnapshotHash,
                            verifier,
                            lifetime.Token);
                        break;
                    }

                    case "credential_pending":
                    {
                        var verifier = credentialStore.GetRequiredVerifier(item.LocalEmployeeNo);
                        await actionClient.CompleteExistingCredentialAsync(
                            item.LocalEmployeeNo,
                            inspection.SnapshotHash,
                            verifier,
                            lifetime.Token);
                        break;
                    }

                    case "new_email_pending":
                    {
                        var verifier = credentialStore.GetRequiredVerifier(item.LocalEmployeeNo);
                        using var verify = new CloudEmployeeEmailVerificationForm(
                            actionClient,
                            item,
                            inspection.SnapshotHash,
                            verifier);
                        if (verify.ShowDialog(this) != DialogResult.OK)
                            return;
                        break;
                    }

                    case "conflict":
                        continue;

                    default:
                        throw new InvalidDataException($"未知的帳號轉換狀態：{item.State}");
                }
            }

            await ReloadAfterActionAsync();
        });
    }

    private async Task ReloadAfterActionAsync()
    {
        var (transitionClient, _, authorityClient) = CreateClients();
        var localEmployees = repository.Employees.LoadAll();
        inspection = await transitionClient.InspectAsync(localEmployees, lifetime.Token);
        authority = await authorityClient.GetStatusAsync(lifetime.Token);
        ValidateAuthorityIdentity(authority);
        FillInspection();
    }

    private async Task CutoverAsync()
    {
        if (inspection is null) return;
        await RunBusyAsync(async () =>
        {
            var (_, _, authorityClient) = CreateClients();
            var before = await authorityClient.GetStatusAsync(lifetime.Token);
            ValidateAuthorityIdentity(before);

            if (string.Equals(before.State, "cloud", StringComparison.Ordinal))
            {
                await FinalizeLocalCloudAuthorityAsync(authorityClient);
                ShowCompletedState();
                return;
            }

            if (!before.ReadyForCutover)
                throw new InvalidOperationException("仍有帳號尚未完成轉換或中央帳號資料尚未就緒，現在不能切換。");
            if (!string.Equals(before.TransitionSnapshotHash, inspection.SnapshotHash, StringComparison.Ordinal))
                throw new InvalidOperationException("帳號轉換資料已變更，請重新檢查後再切換。");

            // Cache a complete usable snapshot before the server-side cutover. If the
            // following network call becomes ambiguous, Local authority remains active
            // until a later status check confirms the Cloud cutover.
            var snapshot = await authorityClient.GetSnapshotAsync(lifetime.Token);
            ValidateSnapshotIdentity(snapshot);
            repository.CloudEmployees.ReplaceSnapshot(
                snapshot.WorkspaceId,
                snapshot.WorkspaceRevision,
                snapshot.Employees);

            var result = await authorityClient.CutoverAsync(inspection.SnapshotHash, lifetime.Token);
            ValidateAuthorityIdentity(result);
            if (!string.Equals(result.State, "cloud", StringComparison.Ordinal))
                throw new InvalidDataException("Cloud Employee authority 未完成切換。");

            // Read once more after cutover so the offline cache is the newest Cloud
            // authority snapshot, then make the local mode switch last.
            await FinalizeLocalCloudAuthorityAsync(authorityClient);
            ShowCompletedState();
        });
    }

    private async Task FinalizeLocalCloudAuthorityAsync(CloudEmployeeAuthorityClient authorityClient)
    {
        var snapshot = await authorityClient.GetSnapshotAsync(lifetime.Token);
        ValidateSnapshotIdentity(snapshot);
        repository.CloudEmployees.ReplaceSnapshot(
            snapshot.WorkspaceId,
            snapshot.WorkspaceRevision,
            snapshot.Employees);

        repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
        repository.Settings.Save(settings);
        AuthorityReady = true;
    }

    private void FillInspection()
    {
        if (inspection is null || authority is null) return;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var item in inspection.Items)
            {
                var cloudRole = item.MatchedEmployee?.Role ?? item.SuggestedCloudRole;
                var row = new ListViewItem(item.LocalEmployeeNo) { Tag = item };
                row.SubItems.Add(item.LocalName);
                row.SubItems.Add(RoleText(item.LocalRole));
                row.SubItems.Add(RoleText(cloudRole));
                row.SubItems.Add(StateText(item));
                row.SubItems.Add(item.LocalEmail);
                if (item.State == "conflict") row.ForeColor = Color.FromArgb(180, 80, 0);
                else if (item.State == "ready") row.ForeColor = Color.FromArgb(0, 120, 60);
                list.Items.Add(row);
            }
        }
        finally
        {
            list.EndUpdate();
        }

        summary.Text = inspection.ConflictCount > 0
            ? $"共 {inspection.LocalEmployeeCount} 個帳號｜待處理 {inspection.UnresolvedCount}｜身分衝突 {inspection.ConflictCount}。衝突需由 Workspace SUPER_ADMIN 人工確認。"
            : inspection.UnresolvedCount > 0
                ? $"共 {inspection.LocalEmployeeCount} 個帳號｜尚有 {inspection.UnresolvedCount} 個帳號需要完成 Email／登入憑證轉換。"
                : "所有既有帳號已完成確認，可以切換成 Cloud Employee 唯一帳號主資料。";
        UpdateActions();
    }

    private void ShowCompletedState()
    {
        list.Items.Clear();
        summary.Text = "雲端帳號切換已完成。此電腦之後以 Cloud Employee 為唯一帳號主資料；暫時斷網時使用最後同步的安全離線快取。";
        AuthorityReady = true;
        UpdateActions();
    }

    private (CloudEmployeeTransitionClient Transition, CloudEmployeeTransitionActionClient Action, CloudEmployeeAuthorityClient Authority) CreateClients()
    {
        var token = repository.Settings.CloudDeviceToken(settings);
        if (token.Length == 0) throw new InvalidOperationException("本機缺少 Cloud Device Token。");
        var uri = new Uri(settings.CloudBaseUrl, UriKind.Absolute);
        return (
            new CloudEmployeeTransitionClient(httpClient, uri, token),
            new CloudEmployeeTransitionActionClient(httpClient, uri, token),
            new CloudEmployeeAuthorityClient(httpClient, uri, token));
    }

    private void ValidateAuthorityIdentity(CloudEmployeeAuthorityStatus value)
    {
        if (!string.Equals(value.DeviceId, settings.CloudDeviceId, StringComparison.Ordinal)
            || !string.Equals(value.WorkspaceId, settings.CloudWorkspaceId, StringComparison.Ordinal))
            throw new InvalidDataException("Cloud Employee authority 與本機 Workspace／Device identity 不一致。");
    }

    private void ValidateSnapshotIdentity(CloudEmployeeAuthoritySnapshot value)
    {
        if (!string.Equals(value.WorkspaceId, settings.CloudWorkspaceId, StringComparison.Ordinal))
            throw new InvalidDataException("Cloud Employee snapshot 與本機 Workspace identity 不一致。");
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
                MessageBox.Show(this, FriendlyMessage(error), "雲端帳號轉換", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private void UpdateActions()
    {
        refresh.Enabled = !busy && !AuthorityReady;
        process.Enabled = !busy && !AuthorityReady && inspection is not null
            && inspection.Items.Any(item => item.State is "bootstrap_owner_pending" or "credential_pending" or "new_email_pending");
        cutover.Enabled = !busy && !AuthorityReady && authority?.ReadyForCutover == true && inspection is not null;
        close.Enabled = !busy;
    }

    private static string StateText(CloudEmployeeTransitionItem item) => item.State switch
    {
        "ready" when item.MatchKind == "same_employee" => "採用既有雲端帳號",
        "ready" => "已完成",
        "bootstrap_owner_pending" => "建立第一位雲端超管",
        "new_email_pending" => "需要 Email 驗證",
        "credential_pending" => "補齊中央登入憑證",
        "conflict" when item.MatchKind == "employee_no_only" => "員編已存在，待超管確認",
        "conflict" when item.MatchKind == "email_only" => "Email 已存在，待超管確認",
        "conflict" when item.MatchKind == "split" => "員編／Email 指向不同帳號，待超管確認",
        "conflict" => "帳號身分待超管確認",
        _ => item.State,
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
            "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED" => "本機帳號資料已變更，請重新檢查帳號轉換。",
            "EMPLOYEE_AUTHORITY_NOT_READY" => "仍有帳號尚未完成處理，現在不能切換雲端帳號主資料。",
            "EMPLOYEE_CREDENTIAL_CACHE_NOT_READY" => "中央帳號仍有 Email 或登入憑證尚未完成，現在不能建立離線帳號快取。",
            "EMPLOYEE_AUTHORITY_ALREADY_CLOUD" => "Cloud 已完成帳號切換，程式會重新下載中央帳號快取。",
            "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud Email 寄送服務尚未完成設定，帳號仍維持轉換中。",
            _ => $"Cloud API 錯誤：{api.Code}\n{api.Message}",
        };
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "雲端帳號轉換" || ShowIcon || AcceptButton is not null || CancelButton != close)
            throw new InvalidOperationException("雲端帳號轉換視窗基本屬性不正確");
        if (list.Columns.Count != 6 || refresh.Text != "重新檢查" || process.Text != "處理可確認帳號" || cutover.Text != "完成雲端切換")
            throw new InvalidOperationException("雲端帳號轉換視窗欄位或動作按鈕不正確");
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
