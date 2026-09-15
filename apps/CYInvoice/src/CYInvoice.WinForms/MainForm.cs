using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class MainForm : Form
{
    private static readonly Size DefaultClientSize = new(1264, 760);
    private const int WmNcHitTest = 0x0084;
    private const int WmSysCommand = 0x0112;
    private const int ScSize = 0xF000;
    private const int HtLeft = 10;
    private const int HtBottomRight = 17;
    private const int HtBorder = 18;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly InvoiceEntryControl invoicePage;
    private readonly RecordsControl recordsPage;
    private readonly Panel banner = new();
    private readonly TableLayoutPanel bannerLayout = new();
    private readonly Label environmentBadgeLabel = new();
    private readonly Label environmentCompanyLabel = new();
    private readonly Label apiLabel = new();
    private readonly TabControl tabs = new NoFocusCueTabControl();
    private readonly Panel tabHost = new();
    private readonly TabPage invoiceTab = new("開立發票");
    private readonly TabPage recordsTab = new("已開立發票清單");
    private readonly Button settingsButton = UiControls.StandardButton("設定");

    public MainForm(bool startupSmokeTest = false)
    {
        Text = $"CY 電子發票 V{ApplicationVersion.Read()}（C# 重製測試版）";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = DefaultClientSize;
        Font = new Font("Microsoft JhengHei UI", 12F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        SizeGripStyle = SizeGripStyle.Hide;
        ShowIcon = true;
        Icon = ApplicationIcon.Load();
        BackColor = Color.FromArgb(245, 245, 245);
        ResizeEnd += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal && ClientSize != DefaultClientSize)
                ClientSize = DefaultClientSize;
        };

        repository = LocalRepository.Open(AppContext.BaseDirectory, new DpapiSecretProtector());
        service = new InvoiceService(repository);
        recordsPage = new RecordsControl(repository, service);
        invoicePage = new InvoiceEntryControl(repository, service, recordsPage.Reload);
        BuildShell();
        UpdateEnvironment();
        if (!startupSmokeTest) Shown += async (_, _) =>
        {
            if (!EnsureInitialSetup()) return;
            UpdateEnvironment();
            invoicePage.RefreshEnvironment();
            await RefreshApiAsync();
        };
    }

    private void BuildShell()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(20, 2, 20, 2) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        banner.Dock = DockStyle.Fill;
        banner.BackColor = Color.FromArgb(236, 246, 255);
        bannerLayout.Dock = DockStyle.Fill;
        bannerLayout.Margin = Padding.Empty;
        bannerLayout.Padding = Padding.Empty;
        bannerLayout.ColumnCount = 3;
        bannerLayout.RowCount = 1;
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        bannerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        environmentBadgeLabel.AutoSize = true;
        environmentBadgeLabel.Anchor = AnchorStyles.Left;
        environmentBadgeLabel.Margin = new Padding(12, 6, 0, 6);
        environmentBadgeLabel.Padding = new Padding(10, 4, 10, 4);
        environmentBadgeLabel.BorderStyle = BorderStyle.FixedSingle;
        environmentBadgeLabel.TextAlign = ContentAlignment.MiddleCenter;
        environmentBadgeLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);

        environmentCompanyLabel.Dock = DockStyle.Fill;
        environmentCompanyLabel.Margin = Padding.Empty;
        environmentCompanyLabel.TextAlign = ContentAlignment.MiddleCenter;
        environmentCompanyLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        environmentCompanyLabel.ForeColor = Color.FromArgb(0, 72, 170);
        environmentCompanyLabel.AutoEllipsis = false;

        apiLabel.Dock = DockStyle.Fill;
        apiLabel.Margin = Padding.Empty;
        apiLabel.TextAlign = ContentAlignment.MiddleRight;
        apiLabel.Padding = new Padding(0, 0, 14, 0);
        bannerLayout.Controls.Add(environmentBadgeLabel, 0, 0);
        bannerLayout.Controls.Add(environmentCompanyLabel, 1, 0);
        bannerLayout.Controls.Add(apiLabel, 2, 0);
        banner.Controls.Add(bannerLayout);

        tabHost.Dock = DockStyle.Fill;
        tabHost.Margin = Padding.Empty;
        tabs.Dock = DockStyle.Fill;
        tabs.Appearance = TabAppearance.Normal;
        tabs.DrawMode = TabDrawMode.Normal;
        tabs.SizeMode = TabSizeMode.Normal;
        tabs.Padding = new Point(18, 7);
        tabs.Multiline = false;
        invoiceTab.BackColor = Color.White;
        recordsTab.BackColor = Color.White;
        invoiceTab.Controls.Add(invoicePage);
        recordsTab.Controls.Add(recordsPage);
        tabs.TabPages.Add(invoiceTab);
        tabs.TabPages.Add(recordsTab);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            UiControls.HideFocusCue(tabs);
            if (tabs.SelectedTab == recordsTab) recordsPage.Reload();
        };
        settingsButton.Margin = Padding.Empty;
        settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        settingsButton.ForeColor = SystemColors.ControlText;
        settingsButton.UseVisualStyleBackColor = true;
        settingsButton.Click += (_, _) => OpenSettings();
        tabHost.Controls.Add(tabs);
        tabHost.Controls.Add(settingsButton);
        tabHost.Resize += (_, _) => PositionSettingsButton();
        tabHost.Layout += (_, _) => PositionSettingsButton();
        root.Controls.Add(banner, 0, 0);
        root.Controls.Add(tabHost, 0, 1);
        Controls.Add(root);
        PositionSettingsButton();
    }

    private void PositionSettingsButton()
    {
        var headerHeight = settingsButton.Height;
        if (tabs.IsHandleCreated && tabs.TabCount > 0)
            headerHeight = Math.Max(settingsButton.Height, tabs.GetTabRect(0).Height);
        settingsButton.SetBounds(
            Math.Max(0, tabHost.ClientSize.Width - settingsButton.Width - 6),
            Math.Max(0, (headerHeight - settingsButton.Height) / 2),
            settingsButton.Width,
            settingsButton.Height);
        settingsButton.BringToFront();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSysCommand && ((long)message.WParam & 0xFFF0L) == ScSize)
            return;

        base.WndProc(ref message);
        if (message.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            var hit = message.Result.ToInt32();
            if (hit >= HtLeft && hit <= HtBottomRight) message.Result = (IntPtr)HtBorder;
        }
    }

    private void OpenSettings()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (!settings.AdminPasswordSet)
        {
            MessageBox.Show(this, "尚未建立設定管理密碼，請先完成首次安全設定。", "無法開啟設定",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using (var unlock = new AdminUnlockForm(settings))
            if (unlock.ShowDialog(this) != DialogResult.OK) return;

        using var form = new SettingsForm(repository);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        UpdateEnvironment();
        invoicePage.RefreshEnvironment();
        recordsPage.Reload();
        _ = RefreshApiAsync();
    }

    private bool EnsureInitialSetup()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (!repository.Settings.InitialSetupRequired(settings)) return true;
        using Form form = !settings.AdminPasswordSet && settings.MoPasswordEncrypted.Length == 0
            ? new InitialSetupForm(repository)
            : new SettingsForm(repository);
        if (form.ShowDialog(this) == DialogResult.OK) return true;
        Close();
        return false;
    }

    internal void VerifySmokeLayout()
    {
        if (tabs.TabPages.Count != 2 || tabs.TabPages[0] != invoiceTab || tabs.TabPages[1] != recordsTab ||
            tabs.DrawMode != TabDrawMode.Normal || tabs.TabStop)
            throw new InvalidOperationException("主頁籤未使用無焦點虛線的原生 TabControl");
        if (Math.Abs(Font.SizeInPoints - 12F) > 0.1F ||
            Math.Abs(environmentCompanyLabel.Font.SizeInPoints - 14F) > 0.1F)
            throw new InvalidOperationException("主畫面與環境標題字級不正確");
        if (Icon is null) throw new InvalidOperationException("主視窗未載入內嵌程式圖示");
        var bannerCenter = banner.ClientSize.Width / 2;
        var companyCenter = environmentCompanyLabel.PointToScreen(new Point(environmentCompanyLabel.Width / 2, 0)).X -
            banner.PointToScreen(Point.Empty).X;
        if (Math.Abs(bannerCenter - companyCenter) > 1 ||
            bannerLayout.GetColumnWidths()[0] != bannerLayout.GetColumnWidths()[2] ||
            bannerLayout.GetColumn(environmentBadgeLabel) != 0 ||
            bannerLayout.GetColumn(environmentCompanyLabel) != 1 ||
            bannerLayout.GetColumn(apiLabel) != 2)
            throw new InvalidOperationException("標題列未使用左右等寬欄位，或公司名稱未固定在正中央");
        PositionSettingsButton();
        var tabHeader = tabs.GetTabRect(0);
        if (settingsButton.Right != tabHost.ClientSize.Width - 6 ||
            Math.Abs((settingsButton.Top + settingsButton.Height / 2) - (tabHeader.Top + tabHeader.Height / 2)) > 2 ||
            !UiControls.HasLogicalSize(settingsButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("設定按鈕未以標準尺寸對齊頁籤標頭右側");
        tabs.SelectedTab = invoiceTab;
        tabs.PerformLayout();
        Application.DoEvents();
        invoicePage.VerifySmokeLayout();
        if (ExcelComRows.AutomationLcid != 1033)
            throw new InvalidOperationException("Excel COM 未使用相容的 en-US LCID 1033");
        tabs.SelectedTab = recordsTab;
        tabs.PerformLayout();
        Application.DoEvents();
        recordsPage.VerifySmokeLayout();
        tabs.SelectedTab = invoiceTab;
        Application.DoEvents();
        using var firstSetup = new InitialSetupForm(repository);
        firstSetup.Show(this);
        firstSetup.PerformLayout();
        Application.DoEvents();
        firstSetup.VerifySmokeLayout();
        firstSetup.Close();
        using var unlock = new AdminUnlockForm(repository.Settings.LoadOrCreate());
        unlock.Show(this);
        unlock.PerformLayout();
        Application.DoEvents();
        unlock.VerifySmokeLayout();
        unlock.Close();
        using var settings = new SettingsForm(repository);
        settings.Show(this);
        settings.PerformLayout();
        Application.DoEvents();
        settings.VerifySmokeLayout();
        settings.Close();
        using var passwordChange = new ChangeAdminPasswordForm(repository, repository.Settings.LoadOrCreate());
        passwordChange.Show(this);
        passwordChange.PerformLayout();
        Application.DoEvents();
        passwordChange.VerifySmokeLayout();
        passwordChange.Close();
    }

    private void UpdateEnvironment()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (settings.Environment == Environments.Production)
        {
            environmentBadgeLabel.Text = "正式環境｜將開立正式發票";
            environmentBadgeLabel.BackColor = Color.FromArgb(255, 238, 238);
            environmentBadgeLabel.ForeColor = Color.FromArgb(166, 32, 32);
            environmentCompanyLabel.Text = $"公司統編 {settings.ProductionInvoice}";
        }
        else
        {
            environmentBadgeLabel.Text = "測試環境｜不會開立正式發票";
            environmentBadgeLabel.BackColor = Color.FromArgb(255, 247, 221);
            environmentBadgeLabel.ForeColor = Color.FromArgb(166, 92, 0);
            environmentCompanyLabel.Text = "光貿測試公司 12345678";
        }
    }

    private async Task RefreshApiAsync()
    {
        apiLabel.Text = "● API 檢查中";
        apiLabel.ForeColor = Color.FromArgb(196, 126, 0);
        try
        {
            await service.HealthCheckAsync();
            apiLabel.Text = "● API 正常";
            apiLabel.ForeColor = Color.FromArgb(0, 155, 72);
        }
        catch (Exception error)
        {
            apiLabel.Text = "● API 異常";
            apiLabel.ForeColor = Color.FromArgb(196, 0, 0);
            apiLabel.AccessibleDescription = error.Message;
        }
    }
}

internal static class ApplicationVersion
{
    public static string Read()
    {
        try
        {
            var value = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "VERSION-CS")).Trim();
            return value.Length == 0 ? "1.1.0-cs.4" : value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "1.1.0-cs.4";
        }
    }
}
