using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class ChangeAdminPasswordForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly TextBox currentPassword = PasswordBox();
    private readonly TextBox newPassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = UiControls.StandardButton("確認變更");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public ChangeAdminPasswordForm(LocalRepository repository, Settings settings)
    {
        this.repository = repository;
        this.settings = settings;
        Text = "設定管理密碼";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 286);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => (settings.AdminPasswordSet ? currentPassword : newPassword).Focus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 204));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 3; index++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fields.Controls.Add(FieldLabel("目前管理密碼"), 0, 0);
        fields.Controls.Add(currentPassword, 1, 0);
        fields.Controls.Add(FieldLabel("新管理密碼"), 0, 1);
        fields.Controls.Add(newPassword, 1, 1);
        fields.Controls.Add(FieldLabel("再次輸入新密碼"), 0, 2);
        fields.Controls.Add(confirmPassword, 1, 2);
        currentPassword.TabIndex = 0;
        newPassword.TabIndex = 1;
        confirmPassword.TabIndex = 2;
        currentPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, newPassword);
        newPassword.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, confirmPassword);
        confirmPassword.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        if (!settings.AdminPasswordSet) UiControls.SetTextBoxLocked(currentPassword, true);

        cancel.DialogResult = DialogResult.Cancel;
        save.Click += SaveClicked;
        save.TabIndex = 3;
        cancel.TabIndex = 4;
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        save.Margin = new Padding(0, 2, 6, 0);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        cancel.Margin = new Padding(6, 2, 0, 0);
        buttons.Controls.Add(save, 0, 0);
        buttons.Controls.Add(cancel, 1, 0);

        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (settings.AdminPasswordSet && !SettingsStore.CheckAdminPassword(settings, currentPassword.Text))
        {
            ValidationError("目前管理密碼不正確", currentPassword);
            return;
        }
        if (newPassword.Text.Length == 0)
        {
            ValidationError("請輸入新管理密碼", newPassword);
            return;
        }
        if (confirmPassword.Text.Length == 0)
        {
            ValidationError("請再次輸入新管理密碼", confirmPassword);
            return;
        }
        if (newPassword.Text != confirmPassword.Text)
        {
            ValidationError("兩次輸入的新管理密碼不一致", confirmPassword);
            return;
        }

        try
        {
            repository.Settings.SetAdminPassword(settings, newPassword.Text);
            repository.Settings.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法設定管理密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        MessageBox.Show(this, message, "無法設定管理密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (!currentPassword.UseSystemPasswordChar || !newPassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar)
            throw new InvalidOperationException("管理密碼變更視窗未遮蔽輸入內容");
        if (AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(save, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("管理密碼變更視窗按鈕或 Enter 分段操作不正確");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
    };

    private static TextBox PasswordBox() => new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        MaxLength = 200,
        Margin = new Padding(3, 12, 3, 12),
    };
}
