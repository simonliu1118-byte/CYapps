using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class AccountManagementForm : Form
{
    private const int WindowWidth = 620;
    private const int WindowHeight = 380;
    private const int EmployeeNoWidth = 82;
    private const int NameWidth = 104;
    private const int RoleWidth = 96;
    private const int StatusWidth = 64;
    private const int EmailMinimumWidth = 180;
    private readonly LocalRepository? repository;
    private readonly EmployeeStore employees;
    private readonly EmployeeAccount actor;
    private readonly HttpClient cloudHttpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly NativeListViewHost accountHost = new(10F, 24);
    private readonly Button add = UiControls.StandardButton("新增使用者");
    private readonly Button edit = UiControls.StandardButton("修改資料");
    private readonly Button password = UiControls.StandardButton("重設密碼");
    private readonly Button enabled = UiControls.StandardButton("停用帳號");
    private readonly Button role = UiControls.StandardButton("設為管理員");
    private readonly Button recovery = UiControls.StandardButton("重建復原碼");
    private readonly Button pendingIdentity = UiControls.StandardButton("待確認帳號");
    private readonly Button close = UiControls.StandardButton("關閉");
    private bool resourcesDisposed;

    private ListView List => accountHost.List;
    private bool CloudAuthority => repository?.UsesCloudEmployeeAuthority() == true;

    public AccountManagementForm(EmployeeStore employees, EmployeeAccount actor)
        : this(null, employees, actor)
    {
    }

    public AccountManagementForm(LocalRepository repository, EmployeeAccount actor)
        : this(repository, repository?.Employees ?? throw new ArgumentNullException(nameof(repository)), actor)
    {
    }

    private AccountManagementForm(LocalRepository? repository, EmployeeStore employees, EmployeeAccount actor)
    {
        this.repository = repository;
        this.employees = employees ?? throw new ArgumentNullException(nameof(employees));
        this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Text = "管理員帳號管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Reload();
        Shown += async (_, _) =>
        {
            ResizeListColumns();
            await RefreshPendingIdentityButtonAsync();
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12, 10, 12, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));

        List.HideSelection = false;
        List.MultiSelect = false;
        List.GridLines = true;
        List.Columns.Add("員工編號", EmployeeNoWidth, HorizontalAlignment.Center);
        List.Columns.Add("姓名", NameWidth, HorizontalAlignment.Left);
        List.Columns.Add("Email", EmailMinimumWidth, HorizontalAlignment.Left);
        List.Columns.Add("權限", RoleWidth, HorizontalAlignment.Center);
        List.Columns.Add("狀態", StatusWidth, HorizontalAlignment.Center);
        List.SelectedIndexChanged += (_, _) => UpdateButtons();
        List.DoubleClick += async (_, _) => await EditSelectedAsync();
        accountHost.ViewportChanged += (_, _) => ResizeListColumns();
        root.Controls.Add(accountHost, 0, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        for (var index = 0; index < 4; index++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Place(actions, add, 0, 0);
        Place(actions, edit, 1, 0);
        Place(actions, password, 2, 0);
        Place(actions, enabled, 3, 0);
        Place(actions, role, 0, 1);
        Place(actions, recovery, 1, 1);
        Place(actions, pendingIdentity, 2, 1);
        Place(actions, close, 3, 1);
        pendingIdentity.Visible = false;
        add.Click += async (_, _) => await AddEmployeeAsync();
        edit.Click += async (_, _) => await EditSelectedAsync();
        password.Click += async (_, _) => await PasswordSelectedAsync();
        enabled.Click += async (_, _) => await ToggleEnabledAsync();
        role.Click += async (_, _) => await ToggleRoleAsync();
        recovery.Click += async (_, _) => await RecoveryOrTransferAsync();
        pendingIdentity.Click += async (_, _) => await OpenPendingIdentityAsync();
        close.DialogResult = DialogResult.OK;
        root.Controls.Add(actions, 0, 1);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
    }

    private void ResizeListColumns()
    {
        if (List.Columns.Count != 5 || List.ClientSize.Width <= 0) return;
        var fixedWidth = EmployeeNoWidth + NameWidth + RoleWidth + StatusWidth;
        var available = accountHost.ColumnViewportWidth - fixedWidth;
        accountHost.SetColumnWidths([
            EmployeeNoWidth,
            NameWidth,
            Math.Max(EmailMinimumWidth, available),
            RoleWidth,
            StatusWidth,
        ]);
    }

    private static void Place(TableLayoutPanel panel, Button button, int column, int row)
    {
        button.Anchor = AnchorStyles.None;
        panel.Controls.Add(button, column, row);
    }

    private EmployeeAccount? SelectedAccount =>
        List.SelectedItems.Count == 1 ? List.SelectedItems[0].Tag as EmployeeAccount : null;

    private IReadOnlyList<EmployeeAccount> CurrentAccounts() =>
        CloudAuthority && repository is not null ? repository.LoadAuthorityEmployees() : employees.LoadAll();

    private void Reload(string? selectEmployeeNo = null)
    {
        selectEmployeeNo ??= SelectedAccount?.EmployeeNo;
        List.BeginUpdate();
        try
        {
            List.Items.Clear();
            var rowIndex = 0;
            foreach (var account in CurrentAccounts())
            {
                var item = new ListViewItem(account.EmployeeNo) { Tag = account, UseItemStyleForSubItems = false };
                item.SubItems.Add(account.Name);
                item.SubItems.Add(account.Email);
                item.SubItems.Add(RoleText(account.Role));
                item.SubItems.Add(account.Enabled ? "啟用" : "停用");
                StyleRow(item, rowIndex++, account.Enabled);
                List.Items.Add(item);
                if (account.EmployeeNo == selectEmployeeNo) item.Selected = true;
            }
        }
        finally
        {
            List.EndUpdate();
        }
        ResizeListColumns();
        UpdateButtons();
    }

    private static void StyleRow(ListViewItem row, int index, bool accountEnabled)
    {
        var background = index % 2 == 0 ? Color.White : Color.FromArgb(247, 247, 247);
        var foreground = accountEnabled ? SystemColors.ControlText : Color.FromArgb(130, 130, 130);
        foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
        {
            subItem.BackColor = background;
            subItem.ForeColor = foreground;
        }
    }

    private void UpdateButtons()
    {
        var target = SelectedAccount;
        var hasTarget = target is not null;
        if (CloudAuthority)
        {
            // Cloud mode never treats the account that opened this window as a
            // persistent signed-in user. Every sensitive action re-authenticates
            // at execution time and the server rechecks the current Cloud role.
            add.Enabled = true;
            edit.Enabled = hasTarget;
            password.Enabled = hasTarget;
            enabled.Enabled = hasTarget && target!.Role != EmployeeRoles.SuperAdmin;
            role.Enabled = hasTarget && target!.Role != EmployeeRoles.SuperAdmin;
            password.Text = target?.EmployeeNo == actor.EmployeeNo ? "變更密碼" : "重設密碼";
            enabled.Text = target?.Enabled == false ? "啟用帳號" : "停用帳號";
            role.Text = target?.Role == EmployeeRoles.Admin ? "取消管理員" : "設為管理員";
            recovery.Text = "移交超管權限";
            recovery.Enabled = hasTarget && target!.Role == EmployeeRoles.Admin && target.Enabled;
            return;
        }

        add.Enabled = true;
        edit.Enabled = hasTarget && (target!.Role != EmployeeRoles.SuperAdmin || target.EmployeeNo == actor.EmployeeNo);
        password.Enabled = hasTarget && (target!.EmployeeNo == actor.EmployeeNo || target.Role != EmployeeRoles.SuperAdmin);
        password.Text = target?.EmployeeNo == actor.EmployeeNo ? "變更密碼" : "重設密碼";
        enabled.Enabled = hasTarget && target!.Role != EmployeeRoles.SuperAdmin && target.EmployeeNo != actor.EmployeeNo;
        enabled.Text = target?.Enabled == false ? "啟用帳號" : "停用帳號";
        role.Enabled = hasTarget && target!.Role != EmployeeRoles.SuperAdmin && target.EmployeeNo != actor.EmployeeNo;
        role.Text = target?.Role == EmployeeRoles.Admin ? "取消管理員" : "設為管理員";
        recovery.Text = "重建復原碼";
        recovery.Enabled = actor.Role == EmployeeRoles.SuperAdmin;
    }

    private async Task RefreshPendingIdentityButtonAsync()
    {
        pendingIdentity.Visible = false;
        if (!CloudAuthority || repository is null) return;
        try
        {
            var settings = repository.Settings.LoadOrCreate();
            var token = repository.Settings.CloudDeviceToken(settings);
            if (token.Length == 0 || settings.CloudBaseUrl.Length == 0) return;
            var client = new CloudEmployeeTransitionConflictClient(
                cloudHttpClient,
                new Uri(settings.CloudBaseUrl, UriKind.Absolute),
                token);
            var count = await client.GetCountAsync(lifetime.Token);
            if (IsDisposed || Disposing) return;
            pendingIdentity.Text = $"待確認帳號 {count}";
            pendingIdentity.Visible = count > 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            pendingIdentity.Visible = false;
        }
    }

    private async Task OpenPendingIdentityAsync()
    {
        if (!pendingIdentity.Visible || repository is null) return;
        using var form = new CloudEmployeeConflictResolutionForm(repository);
        form.ShowDialog(this);
        await RefreshPendingIdentityButtonAsync();
    }

    private async Task AddEmployeeAsync()
    {
        if (CloudAuthority)
        {
            await AddCloudEmployeeAsync();
            return;
        }

        using var form = new EmployeeEditForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (employees.Find(form.EmployeeNo) is not null)
            {
                MessageBox.Show(this, "此員工編號已存在。", "無法新增使用者",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            employees.CreateEmployee(actor.EmployeeNo, form.EmployeeNo, form.EmployeeName, form.Email, form.Password, form.Role);
            Reload(form.EmployeeNo);
        }
        catch (Exception error)
        {
            ShowOperationError("無法新增使用者", error);
        }
    }

    private async Task AddCloudEmployeeAsync()
    {
        if (repository is null || !CloudAuthority) return;
        using var editForm = new EmployeeEditForm();
        if (editForm.ShowDialog(this) != DialogResult.OK) return;

        using var login = new EmployeeAdminLoginForm(repository, "新增使用者－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        try
        {
            var settings = repository.Settings.LoadOrCreate();
            var token = repository.Settings.CloudDeviceToken(settings);
            if (token.Length == 0 || settings.CloudBaseUrl.Length == 0)
                throw new InvalidOperationException("新增雲端使用者必須在線，且本機需要有效的 Cloud Device identity。");
            var verifier = CloudEmployeeCredentialVerifier.Create(editForm.Password);
            var proposal = new CloudEmployeeCreateProposal(
                editForm.EmployeeNo,
                editForm.EmployeeName,
                editForm.Email,
                editForm.Role,
                verifier);
            var client = new CloudEmployeeManagementClient(
                cloudHttpClient,
                new Uri(settings.CloudBaseUrl, UriKind.Absolute),
                token);
            using var verification = new CloudEmployeeCreationForm(
                client,
                login.AuthenticatedEmployee,
                login.AuthenticatedPassword,
                proposal);
            if (verification.ShowDialog(this) != DialogResult.OK || verification.CreatedEmployee is null) return;

            await RefreshCloudEmployeeSnapshotAsync(settings, token);
            Reload(verification.CreatedEmployee.EmployeeNo);
            MessageBox.Show(this, "新使用者的 Email 已驗證，中央帳號已建立。", "新增完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowCloudOperationError("無法新增使用者", error);
        }
    }

    private async Task RefreshCloudEmployeeSnapshotAsync(Settings settings, string token)
    {
        if (repository is null) return;
        var authority = new CloudEmployeeAuthorityClient(
            cloudHttpClient,
            new Uri(settings.CloudBaseUrl, UriKind.Absolute),
            token);
        var snapshot = await authority.GetSnapshotAsync(lifetime.Token);
        if (!string.Equals(snapshot.WorkspaceId, settings.CloudWorkspaceId, StringComparison.Ordinal))
            throw new InvalidDataException("中央帳號快取回傳的 Workspace identity 與本機不一致。");
        repository.CloudEmployees.ReplaceSnapshot(snapshot.WorkspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
    }

    private (Settings Settings, string Token, CloudEmployeeAccountClient Client) CreateCloudAccountClient()
    {
        if (repository is null || !CloudAuthority)
            throw new InvalidOperationException("這台電腦尚未使用 Cloud Employee 作為帳號主資料。");
        var settings = repository.Settings.LoadOrCreate();
        var token = repository.Settings.CloudDeviceToken(settings);
        if (token.Length == 0 || settings.CloudBaseUrl.Length == 0)
            throw new InvalidOperationException("雲端帳號異動必須在線，且本機需要有效的 Cloud Device identity。");
        return (
            settings,
            token,
            new CloudEmployeeAccountClient(
                cloudHttpClient,
                new Uri(settings.CloudBaseUrl, UriKind.Absolute),
                token));
    }

    private async Task EditSelectedAsync()
    {
        if (CloudAuthority)
        {
            await EditCloudEmployeeAsync();
            return;
        }
        EditLocalSelected();
    }

    private void EditLocalSelected()
    {
        var target = SelectedAccount;
        if (target is null || !edit.Enabled) return;
        var canChangeRole = target.Role != EmployeeRoles.SuperAdmin && target.EmployeeNo != actor.EmployeeNo;
        using var form = new EmployeeEditForm(target, allowRoleChange: canChangeRole);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            employees.UpdateProfile(actor.EmployeeNo, target.EmployeeNo, form.EmployeeName, form.Email);
            if (canChangeRole && form.Role != target.Role)
                employees.SetRole(actor.EmployeeNo, target.EmployeeNo, form.Role);
            Reload(target.EmployeeNo);
        }
        catch (Exception error)
        {
            ShowOperationError("無法修改使用者", error);
            Reload(target.EmployeeNo);
        }
    }

    private async Task EditCloudEmployeeAsync()
    {
        if (repository is null || SelectedAccount is not { } target || !edit.Enabled) return;
        var canChangeRole = target.Role != EmployeeRoles.SuperAdmin;
        using var form = new EmployeeEditForm(target, allowRoleChange: canChangeRole);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var nextRole = target.Role == EmployeeRoles.SuperAdmin ? EmployeeRoles.SuperAdmin : form.Role;
        if (string.Equals(form.EmployeeName, target.Name, StringComparison.Ordinal)
            && string.Equals(form.Email, target.Email, StringComparison.OrdinalIgnoreCase)
            && string.Equals(nextRole, target.Role, StringComparison.Ordinal))
            return;

        using var login = new EmployeeAdminLoginForm(repository, "修改使用者－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;

        try
        {
            var (settings, token, client) = CreateCloudAccountClient();
            var proposal = new CloudEmployeeUpdateProposal(
                target.EmployeeNo,
                form.EmployeeName,
                form.Email,
                nextRole);
            var started = await client.StartUpdateAsync(
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword,
                proposal,
                lifetime.Token);
            CloudEmployeeTransitionIdentity? updated = started.Employee;
            if (started.VerificationRequired)
            {
                if (started.Challenge is null)
                    throw new InvalidDataException("Cloud Employee Email 驗證狀態不完整。");
                using var verification = new CloudEmployeeUpdateVerificationForm(
                    client,
                    login.AuthenticatedEmployee,
                    login.AuthenticatedPassword,
                    proposal,
                    started.Challenge);
                if (verification.ShowDialog(this) != DialogResult.OK || verification.UpdatedEmployee is null) return;
                updated = verification.UpdatedEmployee;
            }
            if (updated is null)
                throw new InvalidDataException("Cloud Employee 修改完成但未回傳帳號資料。");

            await RefreshCloudEmployeeSnapshotAsync(settings, token);
            Reload(updated.EmployeeNo);
            MessageBox.Show(this, "中央帳號資料已更新。", "修改完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowCloudOperationError("無法修改使用者", error);
        }
    }

    private async Task PasswordSelectedAsync()
    {
        if (CloudAuthority)
        {
            await PasswordCloudSelectedAsync();
            return;
        }
        PasswordLocalSelected();
    }

    private void PasswordLocalSelected()
    {
        var target = SelectedAccount;
        if (target is null || !password.Enabled) return;
        if (target.EmployeeNo == actor.EmployeeNo)
        {
            using var change = new EmployeeChangePasswordForm(employees, actor);
            change.ShowDialog(this);
            return;
        }

        using var reset = new EmployeePasswordResetForm(target.EmployeeNo, target.Name);
        if (reset.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            employees.ResetPasswordByAdministrator(actor.EmployeeNo, target.EmployeeNo, reset.NewPassword);
            MessageBox.Show(this, "密碼已重設。", "帳號管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowOperationError("無法重設密碼", error);
        }
    }

    private async Task PasswordCloudSelectedAsync()
    {
        if (repository is null || SelectedAccount is not { } target || !password.Enabled) return;
        using var login = new EmployeeAdminLoginForm(repository, "變更密碼－權限驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;
        if (target.Role == EmployeeRoles.SuperAdmin
            && login.AuthenticatedEmployee.EmployeeNo != target.EmployeeNo)
        {
            MessageBox.Show(this, "超級管理員密碼只能由超級管理員本人變更。", "無法變更密碼",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var reset = new EmployeePasswordResetForm(target.EmployeeNo, target.Name);
        if (reset.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (settings, token, client) = CreateCloudAccountClient();
            var verifier = CloudEmployeeCredentialVerifier.Create(reset.NewPassword);
            var updated = await client.SetPasswordAsync(
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword,
                target.EmployeeNo,
                verifier,
                lifetime.Token);
            await RefreshCloudEmployeeSnapshotAsync(settings, token);
            Reload(updated.EmployeeNo);
            var self = login.AuthenticatedEmployee.EmployeeNo == target.EmployeeNo;
            MessageBox.Show(this, self ? "密碼已變更。" : "密碼已重設。", "帳號管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowCloudOperationError("無法變更密碼", error);
        }
    }

    private async Task ToggleEnabledAsync()
    {
        if (CloudAuthority)
        {
            await ToggleCloudEnabledAsync();
            return;
        }
        ToggleLocalEnabled();
    }

    private void ToggleLocalEnabled()
    {
        var target = SelectedAccount;
        if (target is null || !enabled.Enabled) return;
        var next = !target.Enabled;
        var action = next ? "啟用" : "停用";
        if (MessageBox.Show(this, $"確定要{action} {target.EmployeeNo} {target.Name}?", "帳號管理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            employees.SetEnabled(actor.EmployeeNo, target.EmployeeNo, next);
            Reload(target.EmployeeNo);
        }
        catch (Exception error)
        {
            ShowOperationError($"無法{action}帳號", error);
            Reload(target.EmployeeNo);
        }
    }

    private async Task ToggleCloudEnabledAsync()
    {
        if (repository is null || SelectedAccount is not { } target || !enabled.Enabled) return;
        var next = !target.Enabled;
        var action = next ? "啟用" : "停用";
        if (MessageBox.Show(this, $"確定要{action} {target.EmployeeNo} {target.Name}?", "帳號管理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        using var login = new EmployeeAdminLoginForm(repository, $"{action}帳號－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;
        if (login.AuthenticatedEmployee.EmployeeNo == target.EmployeeNo)
        {
            MessageBox.Show(this, "管理員不能變更自己的啟用狀態。", $"無法{action}帳號",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var (settings, token, client) = CreateCloudAccountClient();
            var updated = await client.SetEnabledAsync(
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword,
                target.EmployeeNo,
                next,
                lifetime.Token);
            await RefreshCloudEmployeeSnapshotAsync(settings, token);
            Reload(updated.EmployeeNo);
            MessageBox.Show(this, $"帳號已{action}。", "帳號管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowCloudOperationError($"無法{action}帳號", error);
        }
    }

    private async Task ToggleRoleAsync()
    {
        if (CloudAuthority)
        {
            await ToggleCloudRoleAsync();
            return;
        }
        ToggleLocalRole();
    }

    private void ToggleLocalRole()
    {
        var target = SelectedAccount;
        if (target is null || !role.Enabled) return;
        var nextRole = target.Role == EmployeeRoles.Admin ? EmployeeRoles.Employee : EmployeeRoles.Admin;
        var action = nextRole == EmployeeRoles.Admin ? "設為管理員" : "取消管理員權限";
        if (MessageBox.Show(this, $"確定要將 {target.EmployeeNo} {target.Name} {action}?", "帳號管理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            employees.SetRole(actor.EmployeeNo, target.EmployeeNo, nextRole);
            Reload(target.EmployeeNo);
        }
        catch (Exception error)
        {
            ShowOperationError("無法變更權限", error);
            Reload(target.EmployeeNo);
        }
    }

    private async Task ToggleCloudRoleAsync()
    {
        if (repository is null || SelectedAccount is not { } target || !role.Enabled) return;
        var nextRole = target.Role == EmployeeRoles.Admin ? EmployeeRoles.Employee : EmployeeRoles.Admin;
        var action = nextRole == EmployeeRoles.Admin ? "設為管理員" : "取消管理員權限";
        if (MessageBox.Show(this, $"確定要將 {target.EmployeeNo} {target.Name} {action}?", "帳號管理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        using var login = new EmployeeAdminLoginForm(repository, "變更權限－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;
        if (login.AuthenticatedEmployee.EmployeeNo == target.EmployeeNo)
        {
            MessageBox.Show(this, "管理員不能變更自己的帳號權限。", "無法變更權限",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var (settings, token, client) = CreateCloudAccountClient();
            var proposal = new CloudEmployeeUpdateProposal(
                target.EmployeeNo,
                target.Name,
                target.Email,
                nextRole);
            var started = await client.StartUpdateAsync(
                login.AuthenticatedEmployee.EmployeeNo,
                login.AuthenticatedPassword,
                proposal,
                lifetime.Token);
            if (started.VerificationRequired)
                throw new InvalidDataException("未變更 Email 的權限異動不應要求 Email 驗證。");
            var updated = started.Employee
                ?? throw new InvalidDataException("Cloud Employee 權限變更完成但未回傳帳號資料。");
            await RefreshCloudEmployeeSnapshotAsync(settings, token);
            Reload(updated.EmployeeNo);
            MessageBox.Show(this, action + "完成。", "帳號管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowCloudOperationError("無法變更權限", error);
        }
    }

    private async Task RecoveryOrTransferAsync()
    {
        if (!CloudAuthority)
        {
            RotateRecoveryCode();
            return;
        }
        await TransferSuperAdminAsync();
    }

    private void RotateRecoveryCode()
    {
        if (CloudAuthority || actor.Role != EmployeeRoles.SuperAdmin) return;
        using var confirm = new RotateRecoveryCodeForm(employees, actor);
        if (confirm.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(confirm.NewRecoveryCode)) return;
        using var show = new RecoveryCodeForm(confirm.NewRecoveryCode);
        show.ShowDialog(this);
    }

    private async Task TransferSuperAdminAsync()
    {
        if (repository is null || !CloudAuthority
            || SelectedAccount is not { Role: EmployeeRoles.Admin, Enabled: true } target)
            return;

        using var login = new EmployeeAdminLoginForm(repository, "移交超管權限－超管驗證");
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return;
        if (login.AuthenticatedEmployee.Role != EmployeeRoles.SuperAdmin)
        {
            MessageBox.Show(this, "必須由目前 Workspace 超級管理員本人重新驗證。", "無法移交",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (login.AuthenticatedEmployee.EmployeeNo == target.EmployeeNo)
        {
            MessageBox.Show(this, "超級管理員不能將權限移交給自己。", "無法移交",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var settings = repository.Settings.LoadOrCreate();
            var token = repository.Settings.CloudDeviceToken(settings);
            if (token.Length == 0 || settings.CloudBaseUrl.Length == 0)
                throw new InvalidOperationException("本機缺少可用的 Cloud Device identity。");
            var client = new CloudSuperAdminTransferClient(
                cloudHttpClient,
                new Uri(settings.CloudBaseUrl, UriKind.Absolute),
                token);
            using var transfer = new CloudSuperAdminTransferForm(
                client,
                login.AuthenticatedEmployee,
                login.AuthenticatedPassword,
                target);
            if (transfer.ShowDialog(this) != DialogResult.OK || transfer.Result is null) return;

            await RefreshCloudEmployeeSnapshotAsync(settings, token);
            MessageBox.Show(
                this,
                $"超級管理員已移交給 {transfer.Result.NewSuperAdmin.EmployeeNo} {transfer.Result.NewSuperAdmin.Name}。\n\n" +
                $"{transfer.Result.FormerSuperAdmin.EmployeeNo} 已改為管理員，Workspace Recovery Email 也已切換至新超管的已驗證 Email。",
                "移交完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception error)
        {
            ShowCloudOperationError("無法移交超級管理員", error);
        }
    }

    private void ShowOperationError(string title, Exception error) =>
        MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ShowCloudOperationError(string title, Exception error) =>
        MessageBox.Show(this, FriendlyCloudOperationMessage(error), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static string FriendlyCloudOperationMessage(Exception error)
    {
        if (error is OperationCanceledException)
            return "Cloud 連線逾時或操作已取消，帳號資料尚未變更。";
        if (error is not CloudApiException api) return error.Message;
        return api.Code switch
        {
            "UNAUTHORIZED" => "Cloud Device 驗證失敗，請先重新檢查雲端連線。",
            "MANAGER_REQUIRED" => "管理員帳密驗證失敗，或目前帳號已沒有帳號管理權限。",
            "EMPLOYEE_AUTHENTICATION_FAILED" => "員工帳密驗證失敗。",
            "EMPLOYEE_NOT_FOUND" => "此使用者已不存在於 Workspace，請重新整理帳號資料。",
            "EMPLOYEE_EMAIL_EXISTS" => "這個 Email 已被 Workspace 內其他使用者使用。",
            "SUPER_ADMIN_SELF_REQUIRED" => "超級管理員資料只能由目前超級管理員本人修改。",
            "SUPER_ADMIN_TRANSFER_REQUIRED" => "超級管理員權限只能使用「移交超管權限」變更。",
            "SELF_ROLE_CHANGE_FORBIDDEN" => "管理員不能變更自己的帳號權限。",
            "SUPER_ADMIN_DISABLE_FORBIDDEN" => "Workspace 超級管理員不能停用。",
            "SELF_DISABLE_FORBIDDEN" => "管理員不能變更自己的啟用狀態。",
            "SUPER_ADMIN_PASSWORD_RESET_FORBIDDEN" => "超級管理員密碼只能由超級管理員本人變更。",
            "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud Email 寄送服務尚未完成設定，目前不能變更 Email。",
            "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
            "OTP_NOT_CONFIGURED" => "Cloud Email 驗證尚未完成設定，目前不能變更 Email。",
            "OTP_INVALID" => "Email 驗證碼錯誤。",
            "OTP_EXPIRED" => "Email 驗證碼已過期，請重新寄送。",
            "OTP_ATTEMPTS_EXHAUSTED" => "Email 驗證碼錯誤次數已達上限，請重新開始操作。",
            "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
            _ => $"Cloud API 錯誤：{api.Code}\n{api.Message}",
        };
    }

    internal void VerifySmokeLayout()
    {
        ResizeListColumns();
        var fixedWidth = EmployeeNoWidth + NameWidth + RoleWidth + StatusWidth;
        var expectedEmailWidth = Math.Max(EmailMinimumWidth, accountHost.ColumnViewportWidth - fixedWidth);
        if (Text != "管理員帳號管理" || add.Text != "新增使用者" || ShowIcon || List.View != View.Details || !List.FullRowSelect || List.Columns.Count != 5 ||
            List.Columns[2].Width != expectedEmailWidth || ClientSize.Width != WindowWidth || ClientSize.Height != WindowHeight ||
            AcceptButton is not null || CancelButton != close || !accountHost.UserColumnResizeLocked ||
            !UiControls.HasLogicalSize(add, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(close, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("帳號管理視窗配置不正確");
        if (!CloudAuthority && recovery.Enabled != (actor.Role == EmployeeRoles.SuperAdmin))
            throw new InvalidOperationException("超級管理員復原碼按鈕權限不正確");
        if (!CloudAuthority && pendingIdentity.Visible)
            throw new InvalidOperationException("單機模式不應顯示帳號身分待確認按鈕");
    }

    private static string RoleText(string value) => value switch
    {
        EmployeeRoles.SuperAdmin => "超級管理員",
        EmployeeRoles.Admin => "管理員",
        _ => "一般使用者",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            cloudHttpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
