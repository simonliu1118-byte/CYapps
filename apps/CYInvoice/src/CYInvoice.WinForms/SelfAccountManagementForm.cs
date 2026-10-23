using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SelfAccountManagementForm : Form
{
    private readonly EmployeeStore employees;
    private readonly EmployeeAccount account;
    private readonly Label currentEmail = new() { Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button changePassword = UiControls.StandardButton("更改密碼");
    private readonly Button close = UiControls.StandardButton("關閉");

    public SelfAccountManagementForm(EmployeeStore employees, EmployeeAccount account)
    {
        this.employees = employees;
        this.account = account;
        Text = "帳號管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(485, 185);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(14, 12, 14, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(new Label { Text = $"{account.EmployeeNo}  {account.Name}", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10F, FontStyle.Bold) }, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        fields.Controls.Add(new Label { Text = "目前 Email", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        currentEmail.Text = account.Email;
        fields.Controls.Add(currentEmail, 1, 0);
        root.Controls.Add(fields, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        close.DialogResult = DialogResult.Cancel;
        changePassword.Click += (_, _) => ChangePassword();
        buttons.Controls.Add(close);
        buttons.Controls.Add(changePassword);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = close;
    }

    private void ChangePassword()
    {
        using var form = new EmployeeChangePasswordForm(employees, account);
        if (form.ShowDialog(this) == DialogResult.OK)
            MessageBox.Show(this, "密碼已變更。", "帳號管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

}
