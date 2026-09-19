using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SuperAdminRecoveryForm : Form
{
    private const int WindowWidth = 390;
    private const int HeaderHeight = 44;
    private const int FieldRowHeight = 38;
    private const int ActionRowHeight = 46;
    private readonly EmployeeStore employees;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox recoveryCode = UiControls.TextBox(64);
    private readonly TextBox newPassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button reset = UiControls.StandardButton("重設密碼");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public SuperAdminRecoveryForm(EmployeeStore employees)
    {
        this.employees = employees;
        Text = "超級管理員密碼復原";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        BuildLayout();
        Shown += (_, _) => employeeNo.Focus();
    }

    private static int CalculateHeight() => 20 + HeaderHeight + FieldRowHeight * 4 + ActionRowHeight;

    private void BuildLayout()
    {
        foreach (var field in new[] { employeeNo, recoveryCode, newPassword, confirmPassword }) ConfigureInputField(field);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight * 4));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));

        root.Controls.Add(new Label
        {
            Text = "僅供超級管理員使用；一般帳戶請由管理員重設密碼。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(88, 88, 88),
            Margin = Padding.Empty,
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 4; index++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.Controls.Add(FieldLabel("員工編號"), 0, 0);
        fields.Controls.Add(employeeNo, 1, 0);
        fields.Controls.Add(FieldLabel("復原碼"), 0, 1);
        fields.Controls.Add(recoveryCode, 1, 1);
        fields.Controls.Add(FieldLabel("新密碼"), 0, 2);
        fields.Controls.Add(newPassword, 1, 2);
        fields.Controls.Add(FieldLabel("再次輸入"), 0, 3);
        fields.Controls.Add(confirmPassword, 1, 3);
        employeeNo.TabIndex = 0;
        recoveryCode.TabIndex = 1;
        newPassword.TabIndex = 2;
        confirmPassword.TabIndex = 3;
        employeeNo.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, recoveryCode);
        recoveryCode.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, newPassword);
        newPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, confirmPassword);
        confirmPassword.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        root.Controls.Add(fields, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        reset.Click += ResetClicked;
        cancel.DialogResult = DialogResult.Cancel;
        reset.TabIndex = 4;
        cancel.TabIndex = 5;
        buttons.Controls.Add(reset);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

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

    private void ResetClicked(object? sender, EventArgs eventArgs)
    {
        if (employeeNo.Text.Trim().Length != 4 || !employeeNo.Text.Trim().All(char.IsDigit))
        {
            ValidationError("員工編號或復原碼錯誤", employeeNo);
            return;
        }
        if (recoveryCode.Text.Trim().Length == 0)
        {
            ValidationError("員工編號或復原碼錯誤", recoveryCode);
            return;
        }
        if (newPassword.Text.Length == 0)
        {
            ValidationError("請輸入新密碼", newPassword);
            return;
        }
        if (confirmPassword.Text.Length == 0 || newPassword.Text != confirmPassword.Text)
        {
            ValidationError("兩次輸入的新密碼不一致", confirmPassword);
            return;
        }

        try
        {
            var replacementCode = employees.ResetSuperAdminPasswordWithRecoveryCode(
                employeeNo.Text,
                recoveryCode.Text,
                newPassword.Text);
            using var codeForm = new RecoveryCodeForm(replacementCode);
            codeForm.ShowDialog(this);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException)
        {
            ValidationError("員工編號或復原碼錯誤", recoveryCode);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法重設密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        reset.PerformClick();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "無法重設密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (Icon is null || employeeNo.MaxLength != 4 || !newPassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar ||
            employeeNo.TextAlign != HorizontalAlignment.Left || recoveryCode.TextAlign != HorizontalAlignment.Left ||
            newPassword.TextAlign != HorizontalAlignment.Left || confirmPassword.TextAlign != HorizontalAlignment.Left ||
            ClientSize.Width != WindowWidth || ClientSize.Height != CalculateHeight() ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(reset, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("超級管理員復原視窗配置不正確");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static TextBox PasswordBox()
    {
        var field = UiControls.TextBox(200);
        field.UseSystemPasswordChar = true;
        return field;
    }

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 4, 0, 0);
    }
}
