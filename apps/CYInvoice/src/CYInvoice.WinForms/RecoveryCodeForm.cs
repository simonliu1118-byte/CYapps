namespace CYInvoice.WinForms;

internal sealed class RecoveryCodeForm : Form
{
    private readonly TextBox recoveryCode = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        TextAlign = HorizontalAlignment.Center,
        Font = new Font("Consolas", 12F, FontStyle.Bold),
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly Button copy = UiControls.StandardButton("複製復原碼");
    private readonly Button saved = UiControls.StandardButton("我已保存");

    public RecoveryCodeForm(string code)
    {
        recoveryCode.Text = code;
        Text = "超級管理員復原碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 246);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => saved.Focus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 14, 18, 14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "這組復原碼只會完整顯示這一次。請保存於安全的公司文件或離線位置。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(150, 45, 45),
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
        }, 0, 0);

        recoveryCode.Margin = new Padding(0, 6, 0, 6);
        root.Controls.Add(recoveryCode, 0, 1);

        root.Controls.Add(new Label
        {
            Text = "若超級管理員密碼與復原碼同時遺失，單機版不提供一般管理員後門。重新產生新碼後，舊碼會立即失效。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(88, 88, 88),
            Font = new Font(Font.FontFamily, 8.5F),
        }, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        copy.Click += (_, _) => CopyCode();
        saved.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(copy);
        buttons.Controls.Add(saved);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        AcceptButton = saved;
        ControlBox = false;
    }

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(recoveryCode.Text);
            MessageBox.Show(this, "復原碼已複製到剪貼簿。", "已複製",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法複製復原碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal void VerifySmokeLayout()
    {
        if (!recoveryCode.ReadOnly || recoveryCode.UseSystemPasswordChar || !ControlBox || AcceptButton != saved ||
            !UiControls.HasLogicalSize(copy, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(saved, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("復原碼顯示視窗配置不正確");
    }

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 7, 0, 0);
    }
}
