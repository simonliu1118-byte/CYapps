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
    private readonly EmployeeStore employees;
    private readonly EmployeeAccount actor;
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        MultiSelect = false,
    };
    private readonly Button add = UiControls.StandardButton("新增員工");
    private readonly Button edit = UiControls.StandardButton("修改資料");
    private readonly Button password = UiControls.StandardButton("重設密碼");
    private readonly Button enabled = UiControls.StandardButton("停用帳號");
    private readonly Button role = UiControls.StandardButton("設為管理員");
    private readonly Button recovery = UiControls.StandardButton("重建復原碼");
    private readonly Button close = UiControls.StandardButton("關閉");

    public AccountManagementForm(EmployeeStore employees, EmployeeAccount actor)
    {
        this.employees = employees;
        this.actor = actor;
        Text = "帳戶管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = true;
        Icon = ApplicationIcon.Load();
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Reload();
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

        list.Margin = Padding.Empty;
        list.Columns.Add("員工編號", EmployeeNoWidth, HorizontalAlignment.Center);
        list.Columns.Add("姓名", NameWidth, HorizontalAlignment.Left);
        list.Columns.Add("Email", EmailMinimumWidth, HorizontalAlignment.Left);
        list.Columns.Add("權限", RoleWidth, HorizontalAlignment.Center);
        list.Columns.Add("狀態", StatusWidth, HorizontalAlignment.Center);
        list.SelectedIndexChanged += (_, _) => UpdateButtons();
        list.DoubleClick += (_, _) => EditSelected();
        list.SizeChanged += (_, _) => ResizeListColumns();
        list.Layout += (_, _) => ResizeListColumns();
        root.Controls.Add(list, 0, 0);

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
        Place(actions, close, 3, 1);
        add.Click += (_, _) => AddEmployee();
        edit.Click += (_, _) => EditSelected();
        password.Click += (_, _) => PasswordSelected();
        enabled.Click += (_, _) => ToggleEnabled();
        role.Click += (_, _) => ToggleRole();
        recovery.Click += (_, _) => RotateRecoveryCode();
        close.DialogResult = DialogResult.OK;
        root.Controls.Add(actions, 0, 1);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
    }

    private void ResizeListColumns()
    {
        if (list.Columns.Count != 5 || list.ClientSize.Width <= 0) return;
        var fixedWidth = EmployeeNoWidth + NameWidth + RoleWidth + StatusWidth;
        var available = list.ClientSize.Width - fixedWidth - SystemInformation.VerticalScrollBarWidth - 4;
        list.Columns[2].Width = Math.Max(EmailMinimumWidth, available);
    }

    private static void Place(TableLayoutPanel panel, Button button, int column, int row)
    {
        button.Anchor = AnchorStyles.None;
        panel.Controls.Add(button, column, row);
    }

    private EmployeeAccount? SelectedAccount =>
        list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as EmployeeAccount : null;

    private void Reload(string? selectEmployeeNo = null)
    {
        selectEmployeeNo ??= SelectedAccount?.EmployeeNo;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var account in employees.LoadAll())
            {
                var item = new ListViewItem(account.EmployeeNo) { Tag = account };
                item.SubItems.Add(account.Name);
                item.SubItems.Add(account.Email);
                item.SubItems.Add(RoleText(account.Role));
                item.SubItems.Add(account.Enabled ? "啟用" : "停用");
                if (!account.Enabled) item.ForeColor = Color.FromArgb(130, 130, 130);
                list.Items.Add(item);
                if (account.EmployeeNo == selectEmployeeNo) item.Selected = true;
            }
        }
        finally
        {
            list.EndUpdate();
        }
        ResizeListColumns();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var target = SelectedAccount;
        var hasTarget = target is not null;
        edit.Enabled = hasTarget && (target!.Role != EmployeeRoles.SuperAdmin || target.EmployeeNo == actor.EmployeeNo);
        password.Enabled = hasTarget && (target!.EmployeeNo == actor.EmployeeNo || target.Role != EmployeeRoles.SuperAdmin);
        password.Text = target?.EmployeeNo == actor.EmployeeNo ? "變更密碼" : "重設密碼";
        enabled.Enabled = hasTarget && target!.Role != EmployeeRoles.SuperAdmin && target.EmployeeNo != actor.EmployeeNo;
        enabled.Text = target?.Enabled == false ? "啟用帳號" : "停用帳號";
        role.Enabled = hasTarget && target!.Role != EmployeeRoles.SuperAdmin && target.EmployeeNo != actor.EmployeeNo;
        role.Text = target?.Role == EmployeeRoles.Admin ? "取消管理員" : "設為管理員";
        recovery.Enabled = actor.Role == EmployeeRoles.SuperAdmin;
    }

    private void AddEmployee()
    {
        using var form = new EmployeeEditForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (employees.Find(form.EmployeeNo) is not null)
            {
                MessageBox.Show(this, "此員工編號已存在。", "無法新增員工",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            employees.CreateEmployee(actor.EmployeeNo, form.EmployeeNo, form.EmployeeName, form.Email, form.Password, form.Role);
            Reload(form.EmployeeNo);
        }
        catch (Exception error)
        {
            ShowOperationError("無法新增員工", error);
        }
    }

    private void EditSelected()
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
            ShowOperationError("無法修改員工", error);
            Reload(target.EmployeeNo);
        }
    }

    private void PasswordSelected()
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
            MessageBox.Show(this, "密碼已重設。", "帳戶管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            ShowOperationError("無法重設密碼", error);
        }
    }

    private void ToggleEnabled()
    {
        var target = SelectedAccount;
        if (target is null || !enabled.Enabled) return;
        var next = !target.Enabled;
        var action = next ? "啟用" : "停用";
        if (MessageBox.Show(this, $"確定要{action} {target.EmployeeNo} {target.Name}？", "帳戶管理",
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
        var target = SelectedAccount;
        if (target is null || !role.Enabled) return;
        var nextRole = target.Role == EmployeeRoles.Admin ? EmployeeRoles.Employee : EmployeeRoles.Admin;
        var action = nextRole == EmployeeRoles.Admin ? "設為管理員" : "取消管理員權限";
        if (MessageBox.Show(this, $"確定要將 {target.EmployeeNo} {target.Name} {action}？", "帳戶管理",
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
        if (actor.Role != EmployeeRoles.SuperAdmin) return;
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
        var expectedEmailWidth = Math.Max(
            EmailMinimumWidth,
            list.ClientSize.Width - fixedWidth - SystemInformation.VerticalScrollBarWidth - 4);
        if (Text != "帳戶管理" || Icon is null || list.View != View.Details || !list.FullRowSelect || list.Columns.Count != 5 ||
            list.Columns[2].Width != expectedEmailWidth || ClientSize.Width != WindowWidth || ClientSize.Height != WindowHeight ||
            AcceptButton is not null || CancelButton != close ||
            !UiControls.HasLogicalSize(add, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(close, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("帳戶管理視窗配置不正確");
        if (recovery.Enabled != (actor.Role == EmployeeRoles.SuperAdmin))
            throw new InvalidOperationException("超級管理員復原碼按鈕權限不正確");
    }

    private static string RoleText(string value) => value switch
    {
        EmployeeRoles.SuperAdmin => "超級管理員",
        EmployeeRoles.Admin => "管理員",
        _ => "一般員工",
    };
}
