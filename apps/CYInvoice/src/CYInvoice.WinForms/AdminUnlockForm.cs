using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class AdminUnlockForm : Form
{
    private const int CompactButtonWidth = 76;
    private readonly Settings settings;
    private readonly TextBox password = new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        MaxLength = 200,
    };
    private readonly Button unlock = CompactButton("確定");
    private readonly Button cancel = CompactButton("取消");

    public AdminUnlockForm(Settings settings)
    {
        this.settings = settings;
        Text = "驗證設定管理密碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(230, 174);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => password.Focus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 12, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "請輸入設定管理密碼",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        }, 0, 0);
        password.Margin = new Padding(0, 3, 0, 7);
        root.Controls.Add(password, 0, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 7, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        unlock.Anchor = AnchorStyles.None;
        cancel.Anchor = AnchorStyles.None;
        unlock.Click += UnlockClicked;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(unlock, 0, 0);
        buttons.Controls.Add(cancel, 1, 0);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = unlock;
        CancelButton = cancel;
    }

    private void UnlockClicked(object? sender, EventArgs eventArgs)
    {
        if (SettingsStore.CheckAdminPassword(settings, password.Text))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        MessageBox.Show(this, "設定管理密碼不正確", "無法進入設定",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            password.Focus();
            password.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (!password.UseSystemPasswordChar || AcceptButton != unlock || CancelButton != cancel ||
            unlock.Text != "確定" || cancel.Text != "取消" ||
            !UiControls.HasLogicalSize(unlock, CompactButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(cancel, CompactButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("設定管理密碼單次解鎖視窗配置不正確");

        PerformLayout();
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        if (logicalWidth > 240 || unlock.Left < 0 || cancel.Right > ClientSize.Width || password.Right > ClientSize.Width)
            throw new InvalidOperationException("設定管理密碼視窗縮小後控制項配置超出視窗");
    }

    private static Button CompactButton(string text) => new NoFocusCueButton
    {
        Text = text,
        Width = CompactButtonWidth,
        Height = UiControls.StandardButtonHeight,
        Margin = new Padding(4, 2, 4, 2),
        AutoSize = false,
        UseVisualStyleBackColor = true,
    };
}
