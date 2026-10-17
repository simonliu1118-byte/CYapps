using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// V3: the self-service form applies only to Local Employee identities.
// CYID users manage their credentials and email in CYWEB, not in CYInvoice.
internal sealed class SelfAccountManagementForm : Form
{
    private readonly EmployeeStore employees;
    private readonly EmployeeAccount account;
    private readonly Button changePassword = UiControls.StandardButton("更改密碼");
    private readonly Button close = UiControls.StandardButton("關閉");

    public SelfAccountManagementForm(LocalRepository repository, EmployeeAccount account)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.account = account ?? throw new ArgumentNullException(nameof(account));
        if (repository.IdentityProvider.Kind != IdentityProviderKind.Local)
            throw new InvalidOperationException("CYID 帳號管理請至 CYWEB。");
        employees = repository.Employees;
        Text = "帳號管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 160);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(14, 12, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = $"{account.EmployeeNo}  {account.Name}", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Email：" + account.Email, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        }, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 5, 0, 0),
        };
        close.DialogResult = DialogResult.Cancel;
        changePassword.Click += (_, _) =>
        {
            using var form = new EmployeeChangePasswordForm(employees, account);
            if (form.ShowDialog(this) == DialogResult.OK)
                MessageBox.Show(this, "密碼已變更。", "帳號管理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        actions.Controls.Add(close);
        actions.Controls.Add(changePassword);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        CancelButton = close;
    }
}
