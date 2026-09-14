using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class InitialSetupForm : Form
{
    private readonly LocalRepository repository;
    private readonly TextBox moPassword = PasswordBox();
    private readonly TextBox adminPassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = new() { Text = "完成設定", Width = 104, Height = 36 };
    private readonly Button cancel = new() { Text = "取消並關閉", DialogResult = DialogResult.Cancel, Width = 104, Height = 36 };

    public InitialSetupForm(LocalRepository repository)
    {
        this.repository = repository;
        Text = "CYInvoice 首次安全設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(300, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => moPassword.Focus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(new Label
        {
            Text = "首次使用固定進入光貿測試環境，請先完成本機密碼設定。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
        }, 0, 0);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(4, 0, 4, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 3; index++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        }
        fields.Controls.Add(FieldLabel("MO店+ Excel 保護密碼"), 0, 0);
        fields.Controls.Add(moPassword, 0, 1);
        fields.Controls.Add(FieldLabel("設定管理密碼"), 0, 2);
        fields.Controls.Add(adminPassword, 0, 3);
        fields.Controls.Add(FieldLabel("再次輸入管理密碼"), 0, 4);
        fields.Controls.Add(confirmPassword, 0, 5);
        moPassword.TabIndex = 0;
        adminPassword.TabIndex = 1;
        confirmPassword.TabIndex = 2;
        moPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, adminPassword);
        adminPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, confirmPassword);
        confirmPassword.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        root.Controls.Add(fields, 0, 1);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        save.Margin = new Padding(0, 0, 6, 0);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        cancel.Margin = new Padding(6, 0, 0, 0);
        save.TabIndex = 3;
        cancel.TabIndex = 4;
        save.Click += SaveClicked;
        buttons.Controls.Add(save, 0, 0);
        buttons.Controls.Add(cancel, 1, 0);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (moPassword.Text.Length == 0) { ValidationError("請輸入 MO店+ Excel 保護密碼", moPassword); return; }
            if (adminPassword.Text.Length == 0) { ValidationError("請設定管理密碼", adminPassword); return; }
            if (confirmPassword.Text.Length == 0) { ValidationError("請再次輸入管理密碼", confirmPassword); return; }
            if (adminPassword.Text != confirmPassword.Text) { ValidationError("兩次輸入的管理密碼不一致", confirmPassword); return; }

            var settings = repository.Settings.LoadOrCreate();
            if (settings.AdminPasswordSet || settings.MoPasswordEncrypted.Length != 0)
                throw new InvalidOperationException("已存在部分安全設定，請改由一般設定視窗完成驗證");
            settings.Environment = Environments.Test;
            repository.Settings.SetMoPassword(settings, moPassword.Text);
            repository.Settings.SetAdminPassword(settings, adminPassword.Text);
            repository.Settings.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法完成首次設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void AdvanceOnEnter(KeyEventArgs eventArgs, Control next)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        next.Focus();
    }

    private void CompleteOnEnter(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        save.PerformClick();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "無法完成首次設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (moPassword.Parent is null || adminPassword.Parent is null || confirmPassword.Parent is null)
            throw new InvalidOperationException("首次設定未建立三個必要密碼欄位");
        if (!moPassword.UseSystemPasswordChar || !adminPassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar)
            throw new InvalidOperationException("首次設定密碼欄未遮蔽內容");
        if (moPassword.TabIndex != 0 || adminPassword.TabIndex != 1 || confirmPassword.TabIndex != 2 || AcceptButton is not null)
            throw new InvalidOperationException("首次設定鍵盤順序或 Enter 分段操作未建立");
        var saveBounds = RectangleToClient(save.RectangleToScreen(save.ClientRectangle));
        var cancelBounds = RectangleToClient(cancel.RectangleToScreen(cancel.ClientRectangle));
        var center = ClientSize.Width / 2;
        if (ClientSize.Width > 320 || saveBounds.Bottom > ClientSize.Height || cancelBounds.Bottom > ClientSize.Height ||
            saveBounds.Right > cancelBounds.Left ||
            Math.Abs((saveBounds.Left + cancelBounds.Right) / 2 - center) > 4)
            throw new InvalidOperationException(
                $"首次設定窄版視窗或置中雙按鈕配置不正確：完成 {saveBounds}，取消 {cancelBounds}，中線 {center}");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        TabStop = false,
    };

    private static TextBox PasswordBox() => new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        Margin = new Padding(0, 4, 0, 7),
        MaxLength = 200,
    };
}
