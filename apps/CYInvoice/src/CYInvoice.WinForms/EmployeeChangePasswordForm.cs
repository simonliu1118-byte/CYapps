using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class EmployeeChangePasswordForm : Form
{
    private const int WindowWidth = 360;
    private const int HeaderHeight = 30;
    private const int FieldRowHeight = 38;
    private const int ActionRowHeight = 46;
    private readonly EmployeeStore employees;
    private readonly EmployeeAccount account;
    private readonly TextBox currentPassword = PasswordBox();
    private readonly TextBox newPassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = UiControls.StandardButton("變更密碼");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public EmployeeChangePasswordForm(EmployeeStore employees, EmployeeAccount account)
    {
        this.employees = employees;
        this.account = account;
        Text = "變更自己的密碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        BuildLayout();
        Shown += (_, _) => currentPassword.Focus();
    }

    private static int CalculateHeight() => 20 + HeaderHeight + FieldRowHeight * 3 + ActionRowHeight;

    private void BuildLayout()
    {
        foreach (var field in new[] { currentPassword, newPassword, confirmPassword }) ConfigureInputField(field);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight * 3));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(new Label
        {
            Text = $"{account.EmployeeNo}  {account.Name}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 3; index++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.Controls.Add(FieldLabel("目前密碼"), 0, 0);
        fields.Controls.Add(currentPassword, 1, 0);
        fields.Controls.Add(FieldLabel("新密碼"), 0, 1);
        fields.Controls.Add(newPassword, 1, 1);
        fields.Controls.Add(FieldLabel("再次輸入"), 0, 2);
        fields.Controls.Add(confirmPassword, 1, 2);
        currentPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, newPassword);
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
        if (currentPassword.Text.Length == 0)
        {
            ValidationError("請輸入目前密碼", currentPassword);
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
            employees.ChangePassword(account.EmployeeNo, currentPassword.Text, newPassword.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException)
        {
            ValidationError("目前密碼不正確", currentPassword);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法變更密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        save.PerformClick();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "無法變更密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (Icon is null || !currentPassword.UseSystemPasswordChar || !newPassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar ||
            currentPassword.TextAlign != HorizontalAlignment.Left || newPassword.TextAlign != HorizontalAlignment.Left ||
            confirmPassword.TextAlign != HorizontalAlignment.Left || ClientSize.Width != WindowWidth ||
            ClientSize.Height != CalculateHeight() || AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(save, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("員工本人密碼變更視窗配置不正確");
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
