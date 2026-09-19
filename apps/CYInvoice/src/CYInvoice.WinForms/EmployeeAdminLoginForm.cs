using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class EmployeeAdminLoginForm : Form
{
    private const int WindowWidth = 280;
    private const int FieldRowHeight = 38;
    private const int ActionRowHeight = 46;
    private const int CompactButtonWidth = 86;
    private readonly EmployeeStore employees;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly Button login = CompactButton("確定");
    private readonly Button cancel = CompactButton("取消");

    public EmployeeAdminLoginForm(EmployeeStore employees, string title = "管理員驗證")
    {
        this.employees = employees;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, 16 + FieldRowHeight * 2 + ActionRowHeight + 12);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => employeeNo.Focus();
    }

    public EmployeeAccount? AuthenticatedEmployee { get; private set; }
    public string AuthenticatedPassword => AuthenticatedEmployee is null ? string.Empty : password.Text;

    private void BuildLayout()
    {
        password.UseSystemPasswordChar = true;
        ConfigureInputField(employeeNo);
        ConfigureInputField(password);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 8, 16, 8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.Controls.Add(FieldLabel("員工編號"), 0, 0);
        fields.Controls.Add(employeeNo, 1, 0);
        fields.Controls.Add(FieldLabel("密碼"), 0, 1);
        fields.Controls.Add(password, 1, 1);
        employeeNo.TabIndex = 0;
        password.TabIndex = 1;
        employeeNo.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, password);
        password.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        root.Controls.Add(fields, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        login.Click += LoginClicked;
        cancel.DialogResult = DialogResult.Cancel;
        login.TabIndex = 2;
        cancel.TabIndex = 3;
        buttons.Controls.Add(login);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 1);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private static void ConfigureInputField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
        field.TextAlign = HorizontalAlignment.Left;
    }

    private void LoginClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            var account = employees.Authenticate(employeeNo.Text, password.Text);
            if (account is null)
            {
                ValidationError("員工編號或密碼錯誤", password);
                return;
            }
            if (!EmployeeRoles.CanManageAccounts(account.Role))
            {
                MessageBox.Show(this, "權限不足，僅管理員可執行此操作。", "權限不足",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AuthenticatedEmployee = account;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException)
        {
            ValidationError("員工編號或密碼錯誤", employeeNo);
        }
    }

    private static void AdvanceOnEnter(KeyEventArgs eventArgs, Control next)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        next.Focus();
    }

    private void CompleteOnEnter(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        login.PerformClick();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "驗證失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (!password.UseSystemPasswordChar || employeeNo.MaxLength != 4 ||
            employeeNo.TextAlign != HorizontalAlignment.Left || password.TextAlign != HorizontalAlignment.Left ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(login, CompactButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(cancel, CompactButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("管理員登入視窗配置不正確");
        if (ClientSize.Width != WindowWidth || ClientSize.Height > 155)
            throw new InvalidOperationException("管理員登入視窗未維持精簡尺寸");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(0, 0, 8, 0),
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
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 4, 0, 0);
    }
}
