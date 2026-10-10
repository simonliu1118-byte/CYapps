namespace CYInvoice.WinForms;

internal sealed class FirstRunModeForm : Form
{
    public bool DirectCloud { get; private set; }
    private readonly Label detail;

    public FirstRunModeForm()
    {
        Text = "CYInvoice 首次開啟";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 265);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 11F);

        var title = new Label { Text = "請選擇這台電腦的使用方式", Dock = DockStyle.Top,
            Height = 55, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font, FontStyle.Bold) };
        detail = new Label
        {
            Text = "1. 使用單機版\n   建立這台電腦的本機超級管理員。\n\n"
                + "2. 直接加入雲端\n   使用配對碼，或邀請碼與既有超管帳密加入。\n   沿用雲端帳號，不建立本機帳號。",
            Dock = DockStyle.Top,
            Height = 140,
            Padding = new Padding(20, 4, 20, 4),
            TextAlign = ContentAlignment.TopLeft,
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 63,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var cloud = UiControls.StandardButton("直接加入雲端");
        var local = UiControls.StandardButton("使用單機版");
        var cancel = UiControls.StandardButton("取消");
        cloud.Width = 145;
        local.Width = 130;
        cancel.Width = 85;
        cloud.Click += (_, _) => { DirectCloud = true; DialogResult = DialogResult.OK; Close(); };
        local.Click += (_, _) => { DirectCloud = false; DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(cloud);
        buttons.Controls.Add(local);
        Controls.Add(detail);
        Controls.Add(title);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    internal void VerifySmokeLayout()
    {
        var measured = TextRenderer.MeasureText(detail.Text, detail.Font,
            new Size(detail.ClientSize.Width - detail.Padding.Horizontal, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (measured.Height > detail.ClientSize.Height - detail.Padding.Vertical)
            throw new InvalidOperationException("首次使用兩點說明被裁切");
        if (!detail.Text.Contains("1. 使用單機版\n", StringComparison.Ordinal)
            || !detail.Text.Contains("2. 直接加入雲端\n", StringComparison.Ordinal))
            throw new InvalidOperationException("首次使用說明缺少兩點分流");
    }
}
