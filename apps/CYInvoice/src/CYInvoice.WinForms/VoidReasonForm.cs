namespace CYInvoice.WinForms;

internal sealed class VoidReasonForm : Form
{
    private readonly TextBox reason = UiControls.TextBox(30);
    private readonly Button next = UiControls.StandardButton("下一步");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public VoidReasonForm()
    {
        Text = "發票作廢";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 158);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        BuildLayout();
        Shown += (_, _) => reason.Focus();
    }

    public string Reason => reason.Text.Trim();

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "作廢原因（最多 15 字）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        reason.Dock = DockStyle.Fill;
        reason.Margin = new Padding(0, 4, 0, 8);
        reason.TabIndex = 0;
        reason.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            next.PerformClick();
        };
        root.Controls.Add(reason, 0, 1);

        next.Click += (_, _) => Submit();
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(next);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void Submit()
    {
        var value = reason.Text.Trim();
        var length = value.EnumerateRunes().Count();
        if (length == 0)
        {
            ValidationError("請輸入作廢原因。");
            return;
        }
        if (length > 15)
        {
            ValidationError("作廢原因最多 15 字。");
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ValidationError(string message)
    {
        MessageBox.Show(this, message, "資料未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            reason.Focus();
            reason.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "發票作廢" || AcceptButton is not null || CancelButton != cancel)
            throw new InvalidOperationException("作廢原因視窗基本配置不正確");
        if (reason.MaxLength < 15 || !UiControls.HasLogicalSize(next, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("作廢原因欄位或按鈕尺寸不正確");
    }
}
