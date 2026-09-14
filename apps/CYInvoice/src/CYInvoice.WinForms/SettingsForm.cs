using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SettingsForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly RadioButton test = new() { Text = "光貿測試環境", AutoSize = true };
    private readonly RadioButton production = new() { Text = "正式公司", AutoSize = true };
    private readonly TextBox invoice = UiControls.TextBox(8);
    private readonly TextBox appKey = UiControls.TextBox(200);
    private readonly TextBox moPassword = UiControls.TextBox(200);
    private readonly TextBox newPassword = UiControls.TextBox(200);
    private readonly TextBox confirmPassword = UiControls.TextBox(200);
    private Label testAccountText = null!;
    private Label invoiceLabel = null!;
    private Label appKeyLabel = null!;

    public SettingsForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "CYInvoice 設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 430);
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
        foreach (var box in new[] { appKey, moPassword, newPassword, confirmPassword }) box.UseSystemPasswordChar = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var environmentGroup = new GroupBox { Text = "使用環境", Dock = DockStyle.Fill };
        var environment = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(8) };
        environment.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        environment.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        environment.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        environment.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        environment.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        environment.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        environment.Controls.Add(test, 0, 0);
        environment.SetColumnSpan(test, 2);
        testAccountText = UiControls.Label("測試帳號由光貿固定提供，不可修改。");
        testAccountText.Margin = Padding.Empty;
        environment.Controls.Add(testAccountText, 1, 0);
        environment.SetColumnSpan(testAccountText, 2);
        environment.Controls.Add(production, 0, 1);
        invoiceLabel = UiControls.Label("統編");
        appKeyLabel = UiControls.Label("App Key");
        invoiceLabel.Margin = Padding.Empty;
        appKeyLabel.Margin = Padding.Empty;
        environment.Controls.Add(invoiceLabel, 1, 1);
        AlignEnvironmentField(invoice);
        environment.Controls.Add(invoice, 2, 1);
        environment.Controls.Add(appKeyLabel, 1, 2);
        AlignEnvironmentField(appKey);
        environment.Controls.Add(appKey, 2, 2);
        test.CheckedChanged += (_, _) => UpdateEnvironmentFields();
        production.CheckedChanged += (_, _) => UpdateEnvironmentFields();
        environmentGroup.Controls.Add(environment);

        var platformGroup = new GroupBox { Text = "平台檔案密碼", Dock = DockStyle.Fill };
        var platform = TwoColumnLayout();
        platform.Controls.Add(UiControls.Label("MO店+ Excel 保護密碼"), 0, 0);
        platform.Controls.Add(moPassword, 1, 0);
        platformGroup.Controls.Add(platform);

        var passwordGroup = new GroupBox { Text = "設定管理密碼", Dock = DockStyle.Fill };
        var passwords = TwoColumnLayout();
        passwords.Controls.Add(UiControls.Label(settings.AdminPasswordSet ? "新密碼（不更改可留白）" : "建立管理密碼"), 0, 0);
        passwords.Controls.Add(newPassword, 1, 0);
        passwords.Controls.Add(UiControls.Label("再次輸入新密碼"), 0, 1);
        passwords.Controls.Add(confirmPassword, 1, 1);
        passwordGroup.Controls.Add(passwords);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 7, 0, 0) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 95, Height = 34 };
        var save = new Button { Text = "儲存設定", Width = 110, Height = 34 };
        save.Click += SaveClicked;
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

    private static TableLayoutPanel TwoColumnLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static void AlignEnvironmentField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3);
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
            : "留白會保留目前已儲存的 MO店+ Excel 保護密碼。";
    }

    private void UpdateEnvironmentFields()
    {
        invoice.Enabled = production.Checked;
        appKey.Enabled = production.Checked;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (!settings.AdminPasswordSet || newPassword.Text.Length != 0 || confirmPassword.Text.Length != 0)
            {
                if (newPassword.Text.Length == 0 || newPassword.Text != confirmPassword.Text)
                    throw new InvalidOperationException("兩次輸入的新管理密碼不一致");
                repository.Settings.SetAdminPassword(settings, newPassword.Text);
            }
            settings.Environment = production.Checked ? Environments.Production : Environments.Test;
            settings.ProductionInvoice = invoice.Text.Trim();
            if (appKey.Text.Length != 0) repository.Settings.SetProductionAppKey(settings, appKey.Text);
            if (moPassword.Text.Length != 0) repository.Settings.SetMoPassword(settings, moPassword.Text);
            if (settings.MoPasswordEncrypted.Length == 0) throw new InvalidOperationException("請輸入 MO店+ Excel 保護密碼");
            repository.Settings.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法儲存設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal void VerifySmokeLayout()
    {
        if (invoice.Enabled != production.Checked || appKey.Enabled != production.Checked)
            throw new InvalidOperationException("測試與正式環境欄位鎖定狀態不一致");
        if (settings.ProductionAppKeyEncrypted.Length != 0 &&
            appKey.PlaceholderText != "留白會保留目前已儲存的 App Key。")
            throw new InvalidOperationException("App Key 保留提示未放在輸入欄位內");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        var noteLeft = testAccountText.PointToScreen(Point.Empty).X;
        var invoiceLeft = invoiceLabel.PointToScreen(Point.Empty).X;
        var appKeyLeft = appKeyLabel.PointToScreen(Point.Empty).X;
        if (logicalWidth > 430 || Math.Abs(noteLeft - invoiceLeft) > 2 || Math.Abs(noteLeft - appKeyLeft) > 2)
            throw new InvalidOperationException(
                $"設定視窗寬度或測試帳號說明對齊不正確：邏輯寬 {logicalWidth:F1}px，說明 {noteLeft}px，統編 {invoiceLeft}px，App Key {appKeyLeft}px");
    }
}
