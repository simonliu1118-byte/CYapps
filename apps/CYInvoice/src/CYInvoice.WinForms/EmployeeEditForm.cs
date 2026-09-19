using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class EmployeeEditForm : Form
{
    private readonly bool createMode;
    private readonly bool allowRoleChange;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox name = UiControls.TextBox(80);
    private readonly TextBox email = UiControls.TextBox(160);
    private readonly TextBox password = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly ComboBox role = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(3, 5, 3, 5),
    };
    private readonly Button save = UiControls.StandardButton("儲存");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public EmployeeEditForm(EmployeeAccount? existing = null, bool allowRoleChange = true)
    {
        createMode = existing is null;
        this.allowRoleChange = createMode || allowRoleChange;
        Text = createMode ? "新增員工" : "修改員工";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, createMode ? 392 : 286);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout(existing);
        Shown += (_, _) => (createMode ? employeeNo : name).Focus();
    }

    public string EmployeeNo => employeeNo.Text.Trim();
    public string EmployeeName => name.Text.Trim();
    public string Email => email.Text.Trim();
    public string Password => password.Text;
    public string Role => role.SelectedIndex == 1 ? EmployeeRoles.Admin : EmployeeRoles.Employee;

    private void BuildLayout(EmployeeAccount? existing)
    {
        role.Items.AddRange(new object[] { "一般員工", "管理員" });
        role.SelectedIndex = existing?.Role == EmployeeRoles.Admin ? 1 : 0;
        employeeNo.TextAlign = HorizontalAlignment.Center;

        var fieldCount = createMode ? 6 : 4;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 14, 18, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, createMode ? 306 : 200));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = fieldCount };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < fieldCount; index++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var row = 0;
        fields.Controls.Add(FieldLabel("員工編號"), 0, row);
        fields.Controls.Add(employeeNo, 1, row++);
        fields.Controls.Add(FieldLabel("姓名"), 0, row);
        fields.Controls.Add(name, 1, row++);
        fields.Controls.Add(FieldLabel("Email"), 0, row);
        fields.Controls.Add(email, 1, row++);
        fields.Controls.Add(FieldLabel("權限"), 0, row);
        fields.Controls.Add(role, 1, row++);
        if (createMode)
        {
            fields.Controls.Add(FieldLabel("初始密碼"), 0, row);
            fields.Controls.Add(password, 1, row++);
            fields.Controls.Add(FieldLabel("再次輸入"), 0, row);
            fields.Controls.Add(confirmPassword, 1, row);
        }
        root.Controls.Add(fields, 0, 0);

        if (existing is not null)
        {
            employeeNo.Text = existing.EmployeeNo;
            name.Text = existing.Name;
            email.Text = existing.Email;
            UiControls.SetTextBoxLocked(employeeNo, true);
            if (existing.Role == EmployeeRoles.SuperAdmin)
            {
                role.Items.Clear();
                role.Items.Add("超級管理員");
                role.SelectedIndex = 0;
                role.Enabled = false;
            }
            else
            {
                role.Enabled = this.allowRoleChange;
            }
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        save.Click += SaveClicked;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        var no = employeeNo.Text.Trim();
        if (no.Length != 4 || !no.All(char.IsDigit))
        {
            ValidationError("員工編號必須為 4 碼數字", employeeNo);
            return;
        }
        if (name.Text.Trim().Length == 0)
        {
            ValidationError("員工姓名不可空白", name);
            return;
        }
        if (createMode)
        {
            if (password.Text.Length == 0)
            {
                ValidationError("請輸入初始密碼", password);
                return;
            }
            if (confirmPassword.Text.Length == 0 || password.Text != confirmPassword.Text)
            {
                ValidationError("兩次輸入的密碼不一致", confirmPassword);
                return;
            }
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "資料不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (employeeNo.MaxLength != 4 || AcceptButton is not null || CancelButton != cancel ||
            (createMode && (!password.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar)) ||
            (!createMode && !allowRoleChange && role.Enabled) ||
            !UiControls.HasLogicalSize(save, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("員工編輯視窗配置不正確");
    }

    private static TextBox PasswordBox()
    {
        var field = UiControls.TextBox(200);
        field.UseSystemPasswordChar = true;
        return field;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 7, 0, 0);
    }
}
