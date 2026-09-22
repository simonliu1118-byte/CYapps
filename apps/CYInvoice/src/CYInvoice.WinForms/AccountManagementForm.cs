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
        Text = "帳號管理";
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
        List.DoubleClick += (_, _) => EditSelected();
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
        add.Click += (_, _) => AddEmployee();
        edit.Click += (_, _) => EditSelected();
        password.Click += (_, _) => PasswordSelected();
        enabled.Click += (_, _) => ToggleEnabled();
        role.Click += (_, _) => ToggleRole();
        recovery.Click += (_, _) => RotateRecoveryCode();
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
            // Cloud is the only account authority after cutover. Local EmployeeStore
            // mutations are intentionally disabled; central CRUD/role operations are
            // wired in the following account-management batch.
            add.Enabled = false;
            edit.Enabled = false;
            password.Enabled = false;
            enabled.Enabled = false;
            role.Enabled = false;
            recovery.Enabled = false;
            password.Text = "重設密碼";
            enabled.Text = "停用帳號";
            role.Text = "設為管理員";
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
        recovery.Enabled = actor.Role == EmployeeRoles.SuperAdmin;
    }

    private async Task RefreshPendingIdentityButtonAsync()
    {
        pendingIdentity.Visible = false;
        if (!CloudAuthority || repository is null || actor.Role != EmployeeRoles.SuperAdmin) return;
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
            // Conflict notification is supplemental. A transient Cloud failure must
            // not prevent the user from viewing the synchronized account list.
            pendingIdentity.Visible = false;
        }
    }

    private async Task OpenPendingIdentityAsync()
    {
        if (!pendingIdentity.Visible || repository is null || actor.Role != EmployeeRoles.SuperAdmin) return;
        using var form = new CloudEmployeeConflictResolutionForm(repository);
        form.ShowDialog(this);
        await RefreshPendingIdentityButtonAsync();
    }

    private void AddEmployee()
    {
        if (CloudAuthority) return;
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

    private void EditSelected()
    {
        if (CloudAuthority) return;
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

    private void PasswordSelected()
    {
        if (CloudAuthority) return;
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

    private void ToggleEnabled()
    {
        if (CloudAuthority) return;
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

    private void ToggleRole()
    {
        if (CloudAuthority) return;
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

    private void RotateRecoveryCode()
    {
        if (CloudAuthority || actor.Role != EmployeeRoles.SuperAdmin) return;
        using var confirm = new RotateRecoveryCodeForm(employees, actor);
        if (confirm.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(confirm.NewRecoveryCode)) return;
        using var show = new RecoveryCodeForm(confirm.NewRecoveryCode);
        show.ShowDialog(this);
    }

    private void ShowOperationError(string title, Exception error) =>
        MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    internal void VerifySmokeLayout()
    {
        ResizeListColumns();
        var fixedWidth = EmployeeNoWidth + NameWidth + RoleWidth + StatusWidth;
        var expectedEmailWidth = Math.Max(EmailMinimumWidth, accountHost.ColumnViewportWidth - fixedWidth);
        if (Text != "帳號管理" || add.Text != "新增使用者" || ShowIcon || List.View != View.Details || !List.FullRowSelect || List.Columns.Count != 5 ||
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
