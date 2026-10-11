namespace CYInvoice.WinForms;

internal sealed class CloudDeviceRevokeAuthenticationForm : Form
{
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly Button confirm = UiControls.DangerButton("確認撤銷");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public CloudDeviceRevokeAuthenticationForm(string deviceDisplayName, bool currentDevice)
    {
        Text = currentDevice ? "撤銷目前裝置" : "撤銷可信任裝置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        password.UseSystemPasswordChar = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var warning = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = currentDevice
                ? $"即將撤銷「{deviceDisplayName}」（目前這台電腦）。撤銷後此 Device Token 立即失效。"
                : $"即將撤銷「{deviceDisplayName}」。撤銷後該裝置的 Device Token 立即失效。",
        };
        root.Controls.Add(warning, 0, 0);
        root.Controls.Add(FieldRow("超管員工編號", employeeNo), 0, 1);
        root.Controls.Add(FieldRow("超管密碼", password), 0, 2);

        confirm.Width = 110;
        cancel.Width = 90;
        cancel.DialogResult = DialogResult.Cancel;
        confirm.Click += (_, _) => Confirm();
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(confirm);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        AcceptButton = confirm;
        CancelButton = cancel;
        employeeNo.Focus();
    }

    public string EmployeeNo => employeeNo.Text.Trim();
    public string Password => password.Text;

    private void Confirm()
    {
        if (EmployeeNo.Length != 4 || !EmployeeNo.All(char.IsDigit))
        {
            MessageBox.Show(this, "請輸入 4 碼超級管理員員工編號。", "無法驗證",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            employeeNo.Focus();
            return;
        }
        if (Password.Length == 0)
        {
            MessageBox.Show(this, "請輸入超級管理員密碼。", "無法驗證",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            password.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Control FieldRow(string labelText, Control field)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        row.Controls.Add(field, 1, 0);
        return row;
    }
}
