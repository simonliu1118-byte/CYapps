using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class MainForm : Form
{
    private static readonly Size DefaultClientSize = new(1264, 861);
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
    private readonly Label environmentLabel = new();
    private readonly Label apiLabel = new();
    private readonly TabControl tabs = new();
    private readonly Panel tabHost = new();
    private readonly TabPage invoiceTab = new("開立發票");
    private readonly TabPage recordsTab = new("已開立發票清單");
    private readonly Button settingsButton = new();

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

        var banner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 246, 255) };
        environmentLabel.Dock = DockStyle.Fill;
        environmentLabel.TextAlign = ContentAlignment.MiddleCenter;
        environmentLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        environmentLabel.ForeColor = Color.FromArgb(0, 72, 170);
        apiLabel.Dock = DockStyle.Right;
        apiLabel.Width = 190;
        apiLabel.TextAlign = ContentAlignment.MiddleRight;
        apiLabel.Padding = new Padding(0, 0, 14, 0);
        banner.Controls.Add(environmentLabel);
        banner.Controls.Add(apiLabel);

        tabHost.Dock = DockStyle.Fill;
        tabHost.Margin = Padding.Empty;
        tabs.Dock = DockStyle.Fill;
        tabs.Appearance = TabAppearance.Normal;
        tabs.DrawMode = TabDrawMode.Normal;
        tabs.Multiline = false;
        tabs.Padding = new Point(18, 7);
        invoiceTab.BackColor = Color.White;
        recordsTab.BackColor = Color.White;
        invoiceTab.Controls.Add(invoicePage);
        recordsTab.Controls.Add(recordsPage);
        tabs.TabPages.Add(invoiceTab);
        tabs.TabPages.Add(recordsTab);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == recordsTab) recordsPage.Reload();
        };
        settingsButton.Text = "設定";
        settingsButton.Width = 132;
        settingsButton.Height = 34;
        settingsButton.Margin = Padding.Empty;
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        settingsButton.ForeColor = SystemColors.ControlText;
        settingsButton.UseVisualStyleBackColor = true;
        settingsButton.Click += (_, _) => OpenSettings();
        tabHost.Controls.Add(tabs);
        tabHost.Controls.Add(settingsButton);
        tabHost.Resize += (_, _) => PositionSettingsButton(tabHost);
        tabHost.Layout += (_, _) => PositionSettingsButton(tabHost);
        PositionSettingsButton(tabHost);
        settingsButton.BringToFront();
        root.Controls.Add(banner, 0, 0);
        root.Controls.Add(tabHost, 0, 1);
        Controls.Add(root);
    }

    private void PositionSettingsButton(Control tabHost)
    {
        var headerHeight = settingsButton.Height;
        if (tabs.IsHandleCreated && tabs.TabCount > 0)
            headerHeight = Math.Max(30, tabs.GetTabRect(0).Height);
        settingsButton.SetBounds(
            Math.Max(0, tabHost.ClientSize.Width - settingsButton.Width),
            0,
            settingsButton.Width,
            headerHeight);
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
        if (tabs.TabPages.Count != 2 || tabs.TabPages[0] != invoiceTab || tabs.TabPages[1] != recordsTab)
            throw new InvalidOperationException("主頁籤未使用兩頁原生 TabControl");
        if (Math.Abs(Font.SizeInPoints - 12F) > 0.1F || Math.Abs(environmentLabel.Font.SizeInPoints - 14F) > 0.1F)
            throw new InvalidOperationException("主畫面與環境標題字級不正確");
        if (Icon is null) throw new InvalidOperationException("主視窗未載入內嵌程式圖示");
        PositionSettingsButton(tabHost);
        if (settingsButton.Top != 0 || settingsButton.Right != tabHost.ClientSize.Width ||
            settingsButton.Bottom > tabs.GetTabRect(0).Bottom + 1)
            throw new InvalidOperationException("設定按鈕未貼齊原生頁籤標頭右側");
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
        firstSetup.CreateControl();
        firstSetup.PerformLayout();
        firstSetup.VerifySmokeLayout();
        using var unlock = new AdminUnlockForm(repository.Settings.LoadOrCreate());
        unlock.CreateControl();
        unlock.PerformLayout();
        unlock.VerifySmokeLayout();
        using var settings = new SettingsForm(repository);
        settings.CreateControl();
        settings.PerformLayout();
        settings.VerifySmokeLayout();
    }

    private void UpdateEnvironment()
    {
        var settings = repository.Settings.LoadOrCreate();
        environmentLabel.Text = settings.Environment == Environments.Production
            ? $"正式環境｜公司統編 {settings.ProductionInvoice}｜將開立正式發票"
            : "測試環境｜光貿測試公司 12345678｜不會開立正式發票";
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
