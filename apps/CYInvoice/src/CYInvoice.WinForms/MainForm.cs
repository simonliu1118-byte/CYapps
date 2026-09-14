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
    private readonly Label environmentPrefixLabel = new();
    private readonly Label environmentCompanyLabel = new();
    private readonly Label environmentSuffixLabel = new();
    private readonly Label apiLabel = new();
    private readonly TabControl tabs = new();
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
        ConfigureEnvironmentLabel(environmentPrefixLabel);
        ConfigureEnvironmentLabel(environmentCompanyLabel);
        ConfigureEnvironmentLabel(environmentSuffixLabel);
        apiLabel.TextAlign = ContentAlignment.MiddleRight;
        apiLabel.Padding = new Padding(0, 0, 14, 0);
        banner.Controls.Add(environmentPrefixLabel);
        banner.Controls.Add(environmentCompanyLabel);
        banner.Controls.Add(environmentSuffixLabel);
        banner.Controls.Add(apiLabel);
        banner.Resize += (_, _) => PositionBannerLabels();

        tabHost.Dock = DockStyle.Fill;
        tabHost.Margin = Padding.Empty;
        tabs.Dock = DockStyle.Fill;
        tabs.Appearance = TabAppearance.Normal;
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(154, 38);
        tabs.Multiline = false;
        invoiceTab.BackColor = Color.White;
        recordsTab.BackColor = Color.White;
        invoiceTab.Controls.Add(invoicePage);
        recordsTab.Controls.Add(recordsPage);
        tabs.TabPages.Add(invoiceTab);
        tabs.TabPages.Add(recordsTab);
        tabs.DrawItem += DrawTabHeader;
        tabs.SelectedIndexChanged += (_, _) =>
        {
            tabs.Invalidate();
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

    private void ConfigureEnvironmentLabel(Label label)
    {
        label.AutoSize = false;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        label.ForeColor = Color.FromArgb(0, 72, 170);
    }

    private void PositionBannerLabels()
    {
        if (banner.ClientSize.Width <= 0) return;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var prefixWidth = TextRenderer.MeasureText(environmentPrefixLabel.Text, environmentPrefixLabel.Font, Size.Empty, flags).Width;
        var companyWidth = TextRenderer.MeasureText(environmentCompanyLabel.Text, environmentCompanyLabel.Font, Size.Empty, flags).Width;
        var suffixWidth = TextRenderer.MeasureText(environmentSuffixLabel.Text, environmentSuffixLabel.Font, Size.Empty, flags).Width;
        var companyLeft = (banner.ClientSize.Width - companyWidth) / 2;
        environmentCompanyLabel.SetBounds(companyLeft, 0, companyWidth, banner.ClientSize.Height);
        environmentPrefixLabel.SetBounds(companyLeft - prefixWidth, 0, prefixWidth, banner.ClientSize.Height);
        environmentSuffixLabel.SetBounds(companyLeft + companyWidth, 0, suffixWidth, banner.ClientSize.Height);
        apiLabel.SetBounds(Math.Max(0, banner.ClientSize.Width - 190), 0, 190, banner.ClientSize.Height);
        apiLabel.BringToFront();
    }

    private void DrawTabHeader(object? sender, DrawItemEventArgs eventArgs)
    {
        var selected = eventArgs.Index == tabs.SelectedIndex;
        var bounds = eventArgs.Bounds;
        using (var background = new SolidBrush(selected ? Color.White : Color.FromArgb(245, 245, 245)))
            eventArgs.Graphics.FillRectangle(background, bounds);
        using (var border = new Pen(Color.FromArgb(218, 218, 218)))
        {
            eventArgs.Graphics.DrawLine(border, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom - 1);
            eventArgs.Graphics.DrawLine(border, bounds.Left, bounds.Top, bounds.Right - 1, bounds.Top);
            eventArgs.Graphics.DrawLine(border, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        }
        if (selected)
        {
            using var accent = new SolidBrush(Color.FromArgb(0, 102, 204));
            eventArgs.Graphics.FillRectangle(accent, bounds.Left + 1, bounds.Bottom - 4, Math.Max(1, bounds.Width - 2), 4);
        }
        TextRenderer.DrawText(eventArgs.Graphics, tabs.TabPages[eventArgs.Index].Text, tabs.Font, bounds,
            SystemColors.ControlText, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            tabs.DrawMode != TabDrawMode.OwnerDrawFixed)
            throw new InvalidOperationException("主頁籤未使用原生 TabControl 或核准的標頭樣式");
        if (Math.Abs(Font.SizeInPoints - 12F) > 0.1F ||
            Math.Abs(environmentCompanyLabel.Font.SizeInPoints - 14F) > 0.1F)
            throw new InvalidOperationException("主畫面與環境標題字級不正確");
        if (Icon is null) throw new InvalidOperationException("主視窗未載入內嵌程式圖示");
        PositionBannerLabels();
        var bannerCenter = banner.ClientSize.Width / 2;
        var companyCenter = environmentCompanyLabel.Left + (environmentCompanyLabel.Width / 2);
        if (Math.Abs(bannerCenter - companyCenter) > 1 || apiLabel.Right != banner.ClientSize.Width)
            throw new InvalidOperationException("公司名稱未固定在標題正中央或 API 狀態未獨立靠右");
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
            environmentPrefixLabel.Text = "正式環境｜";
            environmentCompanyLabel.Text = $"公司統編 {settings.ProductionInvoice}";
            environmentSuffixLabel.Text = "｜將開立正式發票";
        }
        else
        {
            environmentPrefixLabel.Text = "測試環境｜";
            environmentCompanyLabel.Text = "光貿測試公司 12345678";
            environmentSuffixLabel.Text = "｜不會開立正式發票";
        }
        PositionBannerLabels();
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
