namespace CYInvoice.WinForms;

internal sealed class EmployeePasswordResetForm : Form
{
    private readonly TextBox newPassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = UiControls.StandardButton("重設密碼");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public EmployeePasswordResetForm(string employeeNo, string employeeName)
    {
        Text = "重設員工密碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(390, 244);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout(employeeNo, employeeName);
        Shown += (_, _) => newPassword.Focus();
    }

    public string NewPassword => newPassword.Text;

    private void BuildLayout(string employeeNo, string employeeName)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = $"{employeeNo}  {employeeName}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
        }, 0, 0);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        fields.Controls.Add(FieldLabel("新密碼"), 0, 0);
        fields.Controls.Add(newPassword, 1, 0);
        fields.Controls.Add(FieldLabel("再次輸入"), 0, 1);
        fields.Controls.Add(confirmPassword, 1, 1);
        newPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, confirmPassword);
        confirmPassword.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        root.Controls.Add(fields, 0, 1);

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
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
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
        DialogResult = DialogResult.OK;
        Close();
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
        save.PerformClick();
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
        if (!newPassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(save, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("員工密碼重設視窗配置不正確");
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
