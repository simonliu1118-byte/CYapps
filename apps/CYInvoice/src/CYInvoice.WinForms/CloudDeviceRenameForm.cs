namespace CYInvoice.WinForms;

internal sealed class CloudDeviceRenameForm : Form
{
    private readonly TextBox deviceName = UiControls.TextBox(120);
    private readonly Button confirm = UiControls.StandardButton("儲存名稱");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public CloudDeviceRenameForm(string currentName)
    {
        Text = "更改裝置名稱";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        deviceName.Text = currentName;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        for (var row = 0; row < 1; row++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var explanation = new Label
        {
            Text = "請輸入新名稱；儲存前會進行密碼驗證。",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(explanation, 0, 0);
        root.SetColumnSpan(explanation, 2);
        var inputs = new[] { deviceName };
        var names = new[] { "裝置名稱" };
        for (var row = 0; row < inputs.Length; row++)
        {
            root.Controls.Add(new Label
            {
                Text = names[row], AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 3, 14, 3),
            }, 0, row + 1);
            inputs[row].Dock = DockStyle.None;
            inputs[row].Anchor = AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(inputs[row], 1, row + 1);
        }
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
        };
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Width = 90;
        confirm.Width = 110;
        actions.Controls.AddRange([cancel, confirm]);
        root.Controls.Add(actions, 0, 2);
        root.SetColumnSpan(actions, 2);
        confirm.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        foreach (var input in inputs) input.TextChanged += (_, _) => UpdateState();
        Controls.Add(root);
        AcceptButton = confirm;
        CancelButton = cancel;
        UpdateState();
    }

    public string DeviceName => deviceName.Text.Trim();

    private void UpdateState() => confirm.Enabled = DeviceName.Length is >= 1 and <= 120
        && !DeviceName.Any(char.IsControl);
}
