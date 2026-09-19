using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class EmployeeAdminLoginForm : Form
{
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
        ClientSize = new Size(300, 214);
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
        employeeNo.TextAlign = HorizontalAlignment.Center;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 14, 16, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        fields.Controls.Add(FieldLabel("員工編號"), 0, 0);
        fields.Controls.Add(employeeNo, 1, 0);
        fields.Controls.Add(FieldLabel("員工密碼"), 0, 1);
        fields.Controls.Add(password, 1, 1);
        employeeNo.TabIndex = 0;
        password.TabIndex = 1;
        employeeNo.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, password);
        password.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        root.Controls.Add(fields, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "僅啟用中的管理員可進入。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(96, 96, 96),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 8.5F),
        }, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        login.Click += LoginClicked;
        cancel.DialogResult = DialogResult.Cancel;
        login.TabIndex = 2;
        cancel.TabIndex = 3;
        buttons.Controls.Add(login);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
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
        if (!password.UseSystemPasswordChar || employeeNo.MaxLength != 4 || AcceptButton is not null ||
            CancelButton != cancel || !UiControls.HasLogicalSize(login, CompactButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(cancel, CompactButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("管理員登入視窗配置不正確");
        if (ClientSize.Width > 310)
            throw new InvalidOperationException("管理員登入視窗未維持精簡寬度");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
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
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 5, 0, 0);
    }
}
