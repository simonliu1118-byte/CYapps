using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class EmployeeEditForm : Form
{
    private const int WindowWidth = 296;
    private const int FieldRowHeight = 34;
    private const int ActionRowHeight = 40;
    private const int CompactButtonWidth = 110;
    private readonly bool createMode;
    private readonly bool allowRoleChange;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox name = UiControls.TextBox(80);
    private readonly TextBox email = UiControls.TextBox(160);
    private readonly TextBox password = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly ComboBox role = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
    };
    private readonly Button save = CompactButton("儲存");
    private readonly Button cancel = CompactButton("取消");

    public EmployeeEditForm(EmployeeAccount? existing = null, bool allowRoleChange = true)
    {
        createMode = existing is null;
        this.allowRoleChange = createMode || allowRoleChange;
        Text = createMode ? "新增員工" : "修改員工";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout(existing);
        Shown += (_, _) => (createMode ? employeeNo : name).Focus();
    }

    public string EmployeeNo => employeeNo.Text.Trim();
    public string EmployeeName => name.Text.Trim();
    public string Email => email.Text.Trim();
    public string Password => password.Text;
    public string Role => role.SelectedIndex == 1 ? EmployeeRoles.Admin : EmployeeRoles.Employee;

    private int FieldCount => createMode ? 6 : 4;
    private int CalculateHeight() => 16 + FieldCount * FieldRowHeight + ActionRowHeight;

    private void BuildLayout(EmployeeAccount? existing)
    {
        role.Items.AddRange(new object[] { "一般使用者", "管理員" });
        role.SelectedIndex = existing?.Role == EmployeeRoles.Admin ? 1 : 0;
        foreach (var field in InputFields()) ConfigureInputField(field);
        ConfigureChoiceField(role);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10, 8, 10, 8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldCount * FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = FieldCount,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < FieldCount; index++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));

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
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Enter || role.DroppedDown) return base.ProcessCmdKey(ref msg, keyData);

        if (createMode && employeeNo.ContainsFocus)
        {
            FocusField(name);
            return true;
        }
        if (name.ContainsFocus)
        {
            FocusField(email);
            return true;
        }
        if (email.ContainsFocus)
        {
            if (role.Enabled) role.Focus();
            else save.PerformClick();
            return true;
        }
        if (role.ContainsFocus)
        {
            if (createMode) FocusField(password);
            else save.PerformClick();
            return true;
        }
        if (createMode && password.ContainsFocus)
        {
            FocusField(confirmPassword);
            return true;
        }
        if (createMode && confirmPassword.ContainsFocus)
        {
            save.PerformClick();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static void FocusField(TextBox field)
    {
        field.Focus();
        field.SelectAll();
    }

    private IEnumerable<TextBox> InputFields()
    {
        yield return employeeNo;
        yield return name;
        yield return email;
        if (createMode)
        {
            yield return password;
            yield return confirmPassword;
        }
    }

    private static void ConfigureInputField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
        field.TextAlign = HorizontalAlignment.Left;
    }

    private static void ConfigureChoiceField(ComboBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
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
        if (email.Text.Trim().Length == 0)
        {
            ValidationError("請輸入 Email", email);
            return;
        }
        if (createMode)
        {
            if (password.Text.Length < 8 || !password.Text.All(char.IsAsciiLetterOrDigit))
            {
                ValidationError("密碼至少 8 碼，且只能使用英文字母或數字", password);
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
        if (ShowIcon || employeeNo.MaxLength != 4 || InputFields().Any(field => field.TextAlign != HorizontalAlignment.Left) ||
            ClientSize.Width != WindowWidth || ClientSize.Height != CalculateHeight() ||
            AcceptButton is not null || CancelButton != cancel ||
            (createMode && (!password.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar)) ||
            (!createMode && !allowRoleChange && role.Enabled) ||
            role.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "一般員工", StringComparison.Ordinal)) ||
            !UiControls.HasLogicalSize(save, CompactButtonWidth, UiControls.StandardButtonHeight))
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
        Margin = new Padding(0, 0, 6, 0),
    };

    private static Button CompactButton(string text) => new NoFocusCueButton
    {
        Text = text,
        Width = CompactButtonWidth,
        Height = UiControls.StandardButtonHeight,
        Margin = new Padding(5, 2, 5, 2),
        AutoSize = false,
        UseVisualStyleBackColor = true,
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 2, 0, 0);
    }
}
