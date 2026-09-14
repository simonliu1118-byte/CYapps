using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class AdminUnlockForm : Form
{
    private readonly Settings settings;
    private readonly TextBox password = new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        MaxLength = 200,
    };
    private readonly Button unlock = UiControls.StandardButton("進入設定");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public AdminUnlockForm(Settings settings)
    {
        this.settings = settings;
        Text = "驗證設定管理密碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 174);
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
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(new Label
        {
            Text = "請輸入設定管理密碼",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        root.Controls.Add(password, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false,
        };
        unlock.Click += UnlockClicked;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(unlock);
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
            !UiControls.HasLogicalSize(unlock, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(cancel, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("設定管理密碼單次解鎖視窗配置不正確");
    }
}
