using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RotateRecoveryCodeForm : Form
{
    private const int WindowWidth = 360;
    private const int HeaderHeight = 44;
    private const int FieldRowHeight = 38;
    private const int ActionRowHeight = 46;
    private readonly EmployeeStore employees;
    private readonly EmployeeAccount account;
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly Button confirm = UiControls.StandardButton("重新產生");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public RotateRecoveryCodeForm(EmployeeStore employees, EmployeeAccount account)
    {
        this.employees = employees;
        this.account = account;
        password.UseSystemPasswordChar = true;
        Text = "重新產生超管復原碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => password.Focus();
    }

    public string? NewRecoveryCode { get; private set; }

    private static int CalculateHeight() => 20 + HeaderHeight + FieldRowHeight + ActionRowHeight;

    private void BuildLayout()
    {
        ConfigureInputField(password);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(new Label
        {
            Text = "重新產生後舊復原碼立即失效，請輸入目前密碼確認。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        }, 0, 0);

        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        field.Controls.Add(FieldLabel("目前密碼"), 0, 0);
        field.Controls.Add(password, 1, 0);
        password.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            confirm.PerformClick();
        };
        root.Controls.Add(field, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        confirm.Click += ConfirmClicked;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(confirm);
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

    private void ConfirmClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            NewRecoveryCode = employees.RotateSuperAdminRecoveryCode(account.EmployeeNo, password.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException)
        {
            MessageBox.Show(this, "目前密碼不正確。", "無法重新產生復原碼",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            password.Focus();
            password.SelectAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法重新產生復原碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal void VerifySmokeLayout()
    {
        if (!password.UseSystemPasswordChar || password.TextAlign != HorizontalAlignment.Left ||
            ClientSize.Width != WindowWidth || ClientSize.Height != CalculateHeight() ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(confirm, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("復原碼輪替確認視窗配置不正確");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 4, 0, 0);
    }
}
