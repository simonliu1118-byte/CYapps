namespace CYInvoice.WinForms;

internal sealed class ReturnReasonForm : Form
{
    private readonly TextBox reason = new() { Multiline = true, MaxLength = 500, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    public string Reason => reason.Text.Trim().ReplaceLineEndings(" ");
    public ReturnReasonForm()
    {
        Text = "取消退回原因"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(390, 190); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(new Label { Text = "請填寫退回原因（必填）", AutoSize = true }, 0, 0);
        root.Controls.Add(reason, 0, 1);
        var confirm = UiControls.StandardButton("確認退回"); var cancel = UiControls.StandardButton("取消");
        cancel.DialogResult = DialogResult.Cancel;
        confirm.Click += (_, _) => { if (Reason.Length == 0) { reason.Focus(); MessageBox.Show(this, "請填寫退回原因。", "資料未完成"); return; } DialogResult = DialogResult.OK; };
        root.Controls.Add(VoidHistoryDetailForm.CenteredActions(cancel, confirm), 0, 2); Controls.Add(root);
        CancelButton = cancel;
    }
}
