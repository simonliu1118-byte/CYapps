namespace CYInvoice.WinForms;

internal sealed class EmployeePasswordResetForm : Form
{
    private const int WindowWidth = 350;
    private const int HeaderHeight = 30;
    private const int FieldRowHeight = 38;
    private const int ActionRowHeight = 46;
    private readonly TextBox newPassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = UiControls.StandardButton("重設密碼");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public EmployeePasswordResetForm(string employeeNo, string employeeName)
    {
        Text = "重設員工密碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        BuildLayout(employeeNo, employeeName);
        Shown += (_, _) => newPassword.Focus();
    }

    public string NewPassword => newPassword.Text;

    private static int CalculateHeight() => 20 + HeaderHeight + FieldRowHeight * 2 + ActionRowHeight;

    private void BuildLayout(string employeeNo, string employeeName)
    {
        ConfigureInputField(newPassword);
        ConfigureInputField(confirmPassword);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(new Label
        {
            Text = $"{employeeNo}  {employeeName}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
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
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
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

    private static void ConfigureInputField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
        field.TextAlign = HorizontalAlignment.Left;
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
        if (Icon is null || !newPassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar ||
            newPassword.TextAlign != HorizontalAlignment.Left || confirmPassword.TextAlign != HorizontalAlignment.Left ||
            ClientSize.Width != WindowWidth || ClientSize.Height != CalculateHeight() ||
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
        Margin = new Padding(0, 0, 8, 0),
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 4, 0, 0);
    }
}
