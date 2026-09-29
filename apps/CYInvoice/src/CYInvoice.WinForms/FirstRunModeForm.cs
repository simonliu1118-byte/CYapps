namespace CYInvoice.WinForms;

internal sealed class FirstRunModeForm : Form
{
    public bool DirectCloud { get; private set; }

    public FirstRunModeForm()
    {
        Text = "CYInvoice 首次開啟";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 215);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 11F);

        var title = new Label { Text = "請選擇這台電腦的使用方式", Dock = DockStyle.Top,
            Height = 55, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font, FontStyle.Bold) };
        var detail = new Label { Text = "使用單機版：建立本機超級管理員。\n直接加入雲端：使用配對碼，或驗證既有 Workspace 超管；不建立本機帳號。",
            Dock = DockStyle.Top, Height = 75, TextAlign = ContentAlignment.MiddleCenter };
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
}
