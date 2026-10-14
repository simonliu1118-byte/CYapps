namespace CYERPAutoInput;

/// <summary>
/// 放紙提醒 (user, 2026-10-14): before the A4 新竹物流 documents print, the batch waits with a
/// very large line — 「請在印表機放入寄件單用紙」 / 「請在印表機放入取貨總表用紙」 — until 好了
/// is pressed. A pause, not a stop: 取消 (or Esc) stops.
/// </summary>
internal sealed class PaperPromptForm : Form
{
    public PaperPromptForm(string paper, string printer)
    {
        Text = "請放紙";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(980, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        TopMost = true;
        Font = new Font("Microsoft JhengHei UI", 12F);

        var big = new Label
        {
            Text = $"請在印表機放入{paper}",
            Font = new Font("Microsoft JhengHei UI", 40F, FontStyle.Bold),
            ForeColor = CyVisualTheme.Danger,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var small = new Label
        {
            Text = $"印表機：{printer}　　放好之後按「好了」繼續列印",
            Dock = DockStyle.Bottom,
            Height = 44,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 64, Padding = new Padding(12) };
        var ok = new CyPrimaryButton { Text = "好了", Size = new Size(140, 42), DialogResult = DialogResult.OK, Font = new Font("Microsoft JhengHei UI", 14F, FontStyle.Bold) };
        var cancel = new CyButton { Text = "取消", Size = new Size(100, 42), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(big);
        Controls.Add(small);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        CyVisualTheme.Apply(this);
        big.ForeColor = CyVisualTheme.Danger;
        Shown += (_, _) => { Activate(); ok.Focus(); };
    }
}
