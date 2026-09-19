using CYInvoice.Core;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SettingsForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly bool requireMoPassword;
    private readonly ToolTip toolTip = new();
    private readonly RadioButton test = new() { Text = "光貿測試環境", AutoSize = true };
    private readonly RadioButton production = new() { Text = "正式公司", AutoSize = true };
    private readonly TextBox invoice = UiControls.TextBox(8);
    private readonly TextBox appKey = UiControls.TextBox(200);
    private readonly TextBox moPassword = UiControls.TextBox(200);
    private readonly Button save = UiControls.StandardButton("儲存設定");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private TableLayoutPanel environmentLayout = null!;
    private Label invoiceLabel = null!;
    private Label appKeyLabel = null!;
    private Label moPasswordLabel = null!;
    private FlowLayoutPanel actionButtons = null!;

    public SettingsForm(LocalRepository repository, bool requireMoPassword = false)
    {
        this.repository = repository;
        this.requireMoPassword = requireMoPassword;
        settings = repository.Settings.LoadOrCreate();
        Text = "CYInvoice 設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 338);
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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var environmentGroup = new GroupBox { Text = "使用環境", Dock = DockStyle.Fill };
        environmentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(8) };
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
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
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        moPasswordLabel = FieldLabel("MO店+");
        platform.Controls.Add(moPasswordLabel, 1, 0);
        platform.Controls.Add(moPassword, 2, 0);
        toolTip.SetToolTip(moPasswordLabel, requireMoPassword
            ? "目前保存的 MO店+ 密碼無法使用，請重新輸入。"
            : "輸入 MO店+ 匯出 Excel 的保護密碼；留白會保留目前已儲存的密碼。");
        platformGroup.Controls.Add(platform);

        cancel.DialogResult = DialogResult.Cancel;
        save.Click += SaveClicked;
        actionButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
        };
        actionButtons.SizeChanged += (_, _) => CenterButtons(actionButtons, 7);
        actionButtons.Controls.Add(save);
        actionButtons.Controls.Add(cancel);

        root.Controls.Add(environmentGroup, 0, 0);
        root.Controls.Add(platformGroup, 0, 1);
        root.Controls.Add(actionButtons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && production.Checked)
        {
            if (invoice.ContainsFocus)
            {
                appKey.Focus();
                appKey.SelectAll();
                return true;
            }

            if (appKey.ContainsFocus)
            {
                save.PerformClick();
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void LoadValues()
    {
        test.Checked = settings.Environment == Environments.Test;
        production.Checked = settings.Environment == Environments.Production;
        invoice.Text = settings.ProductionInvoice;
        appKey.PlaceholderText = settings.ProductionAppKeyEncrypted.Length == 0
            ? ""
            : "留白會保留目前已儲存的 App Key。";
        moPassword.PlaceholderText = requireMoPassword
            ? "請重新輸入 MO店+ Excel 密碼"
            : settings.MoPasswordEncrypted.Length == 0
                ? ""
                : "留白會保存目前已儲存的密碼";
    }

    private void UpdateEnvironmentFields()
    {
        UiControls.SetTextBoxLocked(invoice, !production.Checked);
        UiControls.SetTextBoxLocked(appKey, !production.Checked);
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            settings.Environment = production.Checked ? Environments.Production : Environments.Test;
            settings.ProductionInvoice = invoice.Text.Trim();
            if (appKey.Text.Length != 0) repository.Settings.SetProductionAppKey(settings, appKey.Text);
            if (requireMoPassword && moPassword.Text.Length == 0)
            {
                MessageBox.Show(this, "請重新輸入 MO店+ Excel 保護密碼", "無法儲存設定",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                moPassword.Focus();
                return;
            }
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

    private static void CenterButtons(FlowLayoutPanel panel, int topPadding)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), topPadding, 0, 0);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(3),
    };

    internal void VerifySmokeLayout()
    {
        if (invoice.ReadOnly != !production.Checked || appKey.ReadOnly != !production.Checked ||
            !invoice.Enabled || !appKey.Enabled)
            throw new InvalidOperationException("測試與正式環境欄位鎖定狀態不一致");
        if (AcceptButton is not null)
            throw new InvalidOperationException("設定視窗不應使用表單預設 AcceptButton，Enter 必須依欄位明確處理");
        if (settings.ProductionAppKeyEncrypted.Length != 0 &&
            appKey.PlaceholderText != "留白會保留目前已儲存的 App Key。")
            throw new InvalidOperationException("App Key 保留提示未放在輸入欄位內");
        if (!requireMoPassword && settings.MoPasswordEncrypted.Length != 0 &&
            moPassword.PlaceholderText != "留白會保存目前已儲存的密碼")
            throw new InvalidOperationException("MO店+ 密碼保留提示文字不正確");
        if (requireMoPassword && moPassword.PlaceholderText != "請重新輸入 MO店+ Excel 密碼")
            throw new InvalidOperationException("MO店+ 密碼重新輸入提示文字不正確");
        if (environmentLayout.GetPositionFromControl(production).Row != 1 ||
            environmentLayout.GetPositionFromControl(invoiceLabel).Column != 1 ||
            environmentLayout.GetPositionFromControl(appKeyLabel).Column != 1 ||
            invoiceLabel.AutoEllipsis || appKeyLabel.AutoEllipsis)
            throw new InvalidOperationException("正式公司、統編與 App Key 未依指定方式排列");
        var environmentX = invoiceLabel.PointToScreen(Point.Empty).X;
        var platformX = moPasswordLabel.PointToScreen(Point.Empty).X;
        if (string.IsNullOrEmpty(toolTip.GetToolTip(moPasswordLabel)) ||
            appKeyLabel.PreferredWidth > appKeyLabel.Width ||
            Math.Abs(environmentX - platformX) > 1 ||
            Math.Abs((actionButtons.Controls.Cast<Control>().Min(control => control.Left) +
                actionButtons.Controls.Cast<Control>().Max(control => control.Right)) / 2 - actionButtons.ClientSize.Width / 2) > 2)
            throw new InvalidOperationException("設定動作、MO店+ 對齊、提示或 App Key 標籤配置不正確");
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
