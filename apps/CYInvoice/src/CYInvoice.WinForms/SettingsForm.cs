using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SettingsForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly ToolTip toolTip = new();
    private readonly RadioButton test = new() { Text = "光貿測試環境", AutoSize = true };
    private readonly RadioButton production = new() { Text = "正式公司", AutoSize = true };
    private readonly TextBox invoice = UiControls.TextBox(8);
    private readonly TextBox appKey = UiControls.TextBox(200);
    private readonly TextBox moPassword = UiControls.TextBox(200);
    private readonly Button forgotPassword = UiControls.StandardButton("忘記密碼");
    private readonly Button changePassword = UiControls.StandardButton("設定密碼");
    private readonly Button save = UiControls.StandardButton("儲存設定");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private TableLayoutPanel environmentLayout = null!;
    private Label invoiceLabel = null!;
    private Label appKeyLabel = null!;
    private Label helpBadge = null!;

    public SettingsForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "CYInvoice 設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 424);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        LoadValues();
        UpdateEnvironmentFields();
    }

    private void BuildLayout()
    {
        appKey.UseSystemPasswordChar = true;
        moPassword.UseSystemPasswordChar = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var environmentGroup = new GroupBox { Text = "使用環境", Dock = DockStyle.Fill };
        environmentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(8) };
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        environmentLayout.Controls.Add(test, 0, 0);
        environmentLayout.SetColumnSpan(test, 3);
        environmentLayout.Controls.Add(production, 0, 1);
        environmentLayout.SetColumnSpan(production, 3);
        invoiceLabel = FieldLabel("統編");
        appKeyLabel = FieldLabel("App Key");
        environmentLayout.Controls.Add(invoiceLabel, 1, 2);
        environmentLayout.Controls.Add(invoice, 2, 2);
        environmentLayout.Controls.Add(appKeyLabel, 1, 3);
        environmentLayout.Controls.Add(appKey, 2, 3);
        test.CheckedChanged += (_, _) => UpdateEnvironmentFields();
        production.CheckedChanged += (_, _) => UpdateEnvironmentFields();
        environmentGroup.Controls.Add(environmentLayout);

        var platformGroup = new GroupBox { Text = "平台檔案密碼", Dock = DockStyle.Fill };
        var platform = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        platform.Controls.Add(FieldLabel("MO店+"), 0, 0);
        platform.Controls.Add(moPassword, 1, 0);
        helpBadge = CreateHelpBadge();
        platform.Controls.Add(helpBadge, 2, 0);
        toolTip.SetToolTip(helpBadge, "輸入 MO店+ 匯出 Excel 的保護密碼；留白會保留目前已儲存的密碼。");
        platformGroup.Controls.Add(platform);

        var passwordGroup = new GroupBox { Text = "設定管理密碼", Dock = DockStyle.Fill };
        var passwordButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(14, 12, 0, 0),
        };
        forgotPassword.Enabled = false;
        toolTip.SetToolTip(forgotPassword, "忘記密碼流程尚未提供。");
        changePassword.Click += (_, _) => ChangeAdminPassword();
        passwordButtons.Controls.Add(forgotPassword);
        passwordButtons.Controls.Add(changePassword);
        passwordGroup.Controls.Add(passwordButtons);

        cancel.DialogResult = DialogResult.Cancel;
        save.Click += SaveClicked;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        root.Controls.Add(environmentGroup, 0, 0);
        root.Controls.Add(platformGroup, 0, 1);
        root.Controls.Add(passwordGroup, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        test.Checked = settings.Environment == Environments.Test;
        production.Checked = settings.Environment == Environments.Production;
        invoice.Text = settings.ProductionInvoice;
        appKey.PlaceholderText = settings.ProductionAppKeyEncrypted.Length == 0
            ? ""
            : "留白會保留目前已儲存的 App Key。";
        moPassword.PlaceholderText = settings.MoPasswordEncrypted.Length == 0
            ? ""
            : "留白會保留目前已儲存的 MO店+ 密碼。";
    }

    private void UpdateEnvironmentFields()
    {
        UiControls.SetTextBoxLocked(invoice, !production.Checked);
        UiControls.SetTextBoxLocked(appKey, !production.Checked);
    }

    private void ChangeAdminPassword()
    {
        using var form = new ChangeAdminPasswordForm(repository, settings);
        form.ShowDialog(this);
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (!settings.AdminPasswordSet)
            {
                MessageBox.Show(this, "請先按「設定密碼」建立設定管理密碼。", "無法儲存設定",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                changePassword.Focus();
                return;
            }
            settings.Environment = production.Checked ? Environments.Production : Environments.Test;
            settings.ProductionInvoice = invoice.Text.Trim();
            if (appKey.Text.Length != 0) repository.Settings.SetProductionAppKey(settings, appKey.Text);
            if (moPassword.Text.Length != 0) repository.Settings.SetMoPassword(settings, moPassword.Text);
            if (settings.MoPasswordEncrypted.Length == 0)
            {
                MessageBox.Show(this, "請輸入 MO店+ Excel 保護密碼", "無法儲存設定",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                moPassword.Focus();
                return;
            }
            repository.Settings.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法儲存設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(3),
    };

    private static Label CreateHelpBadge()
    {
        var badge = new Label
        {
            Text = "?",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            TabStop = false,
            Margin = new Padding(3, 7, 3, 7),
        };
        badge.Paint += (_, eventArgs) =>
        {
            var diameter = Math.Max(2, Math.Min(badge.ClientSize.Width, badge.ClientSize.Height) - 3);
            var left = (badge.ClientSize.Width - diameter) / 2;
            var top = (badge.ClientSize.Height - diameter) / 2;
            using var pen = new Pen(Color.FromArgb(105, 105, 105));
            eventArgs.Graphics.DrawEllipse(pen, left, top, diameter, diameter);
        };
        return badge;
    }

    internal void VerifySmokeLayout()
    {
        if (invoice.ReadOnly != !production.Checked || appKey.ReadOnly != !production.Checked ||
            !invoice.Enabled || !appKey.Enabled)
            throw new InvalidOperationException("測試與正式環境欄位鎖定狀態不一致");
        if (settings.ProductionAppKeyEncrypted.Length != 0 &&
            appKey.PlaceholderText != "留白會保留目前已儲存的 App Key。")
            throw new InvalidOperationException("App Key 保留提示未放在輸入欄位內");
        if (environmentLayout.GetPositionFromControl(production).Row != 1 ||
            environmentLayout.GetPositionFromControl(invoiceLabel).Column != 1 ||
            environmentLayout.GetPositionFromControl(appKeyLabel).Column != 1 ||
            invoiceLabel.AutoEllipsis || appKeyLabel.AutoEllipsis)
            throw new InvalidOperationException("正式公司、統編與 App Key 未依指定方式排列");
        if (forgotPassword.Enabled || !UiControls.HasLogicalSize(changePassword, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            toolTip.GetToolTip(helpBadge).Length == 0)
            throw new InvalidOperationException("設定管理密碼按鈕或 MO店+ 說明提示未建立");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        if (logicalWidth > 430)
            throw new InvalidOperationException("設定視窗未維持精簡寬度");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) toolTip.Dispose();
        base.Dispose(disposing);
    }
}
