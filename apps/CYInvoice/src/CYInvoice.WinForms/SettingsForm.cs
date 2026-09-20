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
    private readonly Button diagnostics = UiControls.StandardButton("系統診斷");
    private readonly Button save = UiControls.StandardButton("儲存設定");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private BufferedTableLayoutPanel environmentLayout = null!;
    private Label invoiceLabel = null!;
    private Label appKeyLabel = null!;
    private Label moPasswordLabel = null!;
    private BufferedFlowLayoutPanel actionButtons = null!;

    public SettingsForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "設定選單";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 338);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        SuspendLayout();
        BuildLayout();
        LoadValues();
        UpdateEnvironmentFields();
        test.CheckedChanged += EnvironmentChanged;
        production.CheckedChanged += EnvironmentChanged;
        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildLayout()
    {
        appKey.UseSystemPasswordChar = true;
        moPassword.UseSystemPasswordChar = true;
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var environmentGroup = new GroupBox { Text = "使用環境", Dock = DockStyle.Fill };
        environmentLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(8),
            Margin = Padding.Empty,
        };
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
        environmentGroup.Controls.Add(environmentLayout);

        var platformGroup = new GroupBox { Text = "平台檔案密碼", Dock = DockStyle.Fill };
        var platform = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(8),
            Margin = Padding.Empty,
        };
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        platform.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        moPasswordLabel = FieldLabel("MO店+");
        platform.Controls.Add(moPasswordLabel, 1, 0);
        platform.Controls.Add(moPassword, 2, 0);
        toolTip.SetToolTip(
            moPasswordLabel,
            "輸入 MO店+ 匯出 Excel 的保護密碼；留白會保留目前已儲存的密碼。\n未設定時只會停用 MO店+ 匯入，不影響其他功能。");
        platformGroup.Controls.Add(platform);

        cancel.DialogResult = DialogResult.Cancel;
        diagnostics.Width = 112;
        save.Width = 112;
        cancel.Width = 112;
        diagnostics.Click += (_, _) =>
        {
            using var form = new SystemDiagnosticsForm(repository);
            form.ShowDialog(this);
        };
        save.Click += SaveClicked;
        actionButtons = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
            Margin = Padding.Empty,
        };
        actionButtons.SizeChanged += (_, _) => CenterButtons(actionButtons, 7);
        actionButtons.Controls.Add(diagnostics);
        actionButtons.Controls.Add(save);
        actionButtons.Controls.Add(cancel);

        root.Controls.Add(environmentGroup, 0, 0);
        root.Controls.Add(platformGroup, 0, 1);
        root.Controls.Add(actionButtons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void EnvironmentChanged(object? sender, EventArgs eventArgs)
    {
        SuspendLayout();
        environmentLayout.SuspendLayout();
        try
        {
            UpdateEnvironmentFields();
        }
        finally
        {
            environmentLayout.ResumeLayout(false);
            ResumeLayout(false);
        }
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
        moPassword.PlaceholderText = settings.MoPasswordEncrypted.Length == 0
            ? "尚未設定"
            : "留白會保存目前已儲存的密碼";
    }

    private void UpdateEnvironmentFields()
    {
        var locked = !production.Checked;
        if (invoice.ReadOnly != locked) UiControls.SetTextBoxLocked(invoice, locked);
        if (appKey.ReadOnly != locked) UiControls.SetTextBoxLocked(appKey, locked);
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            settings.Environment = production.Checked ? Environments.Production : Environments.Test;
            settings.ProductionInvoice = invoice.Text.Trim();
            if (appKey.Text.Length != 0) repository.Settings.SetProductionAppKey(settings, appKey.Text);
            if (moPassword.Text.Length != 0) repository.Settings.SetMoPassword(settings, moPassword.Text);
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
        var left = Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2);
        if (panel.Padding.Left == left && panel.Padding.Top == topPadding) return;
        panel.Padding = new Padding(left, topPadding, 0, 0);
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
        if (Text != "設定選單" || ShowIcon || invoice.ReadOnly != !production.Checked || appKey.ReadOnly != !production.Checked ||
            !invoice.Enabled || !appKey.Enabled)
            throw new InvalidOperationException("設定視窗標題或測試與正式環境欄位鎖定狀態不一致");
        if (AcceptButton is not null)
            throw new InvalidOperationException("設定視窗不應使用表單預設 AcceptButton，Enter 必須依欄位明確處理");
        if (settings.ProductionAppKeyEncrypted.Length != 0 &&
            appKey.PlaceholderText != "留白會保留目前已儲存的 App Key。")
            throw new InvalidOperationException("App Key 保留提示未放在輸入欄位內");
        if (settings.MoPasswordEncrypted.Length != 0 &&
            moPassword.PlaceholderText != "留白會保存目前已儲存的密碼")
            throw new InvalidOperationException("MO店+ 密碼保留提示文字不正確");
        if (settings.MoPasswordEncrypted.Length == 0 && moPassword.PlaceholderText != "尚未設定")
            throw new InvalidOperationException("未設定 MO店+ 密碼提示文字不正確");
        if (environmentLayout.GetPositionFromControl(production).Row != 1 ||
            environmentLayout.GetPositionFromControl(invoiceLabel).Column != 1 ||
            environmentLayout.GetPositionFromControl(appKeyLabel).Column != 1 ||
            invoiceLabel.AutoEllipsis || appKeyLabel.AutoEllipsis)
            throw new InvalidOperationException("正式公司、統編與 App Key 未依指定方式排列");
        var environmentX = invoiceLabel.PointToScreen(Point.Empty).X;
        var platformX = moPasswordLabel.PointToScreen(Point.Empty).X;
        if (string.IsNullOrEmpty(toolTip.GetToolTip(moPasswordLabel)) ||
            appKeyLabel.PreferredWidth > appKeyLabel.Width ||
            diagnostics.Text != "系統診斷" || actionButtons.Controls.Count != 3 ||
            Math.Abs(environmentX - platformX) > 1 ||
            Math.Abs((actionButtons.Controls.Cast<Control>().Min(control => control.Left) +
                actionButtons.Controls.Cast<Control>().Max(control => control.Right)) / 2 - actionButtons.ClientSize.Width / 2) > 2)
            throw new InvalidOperationException("設定動作、系統診斷、MO店+ 對齊、提示或 App Key 標籤配置不正確");
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
