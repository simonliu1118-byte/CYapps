using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class MainForm : Form
{
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly InvoiceEntryControl invoicePage;
    private readonly RecordsControl recordsPage;
    private readonly Label environmentLabel = new();
    private readonly Label apiLabel = new();
    private readonly TabControl tabs = new();
    private readonly TabPage invoiceTab = new("開立發票");
    private readonly TabPage recordsTab = new("已開立發票清單");
    private readonly Button settingsButton = new();

    public MainForm()
    {
        Text = $"CY 電子發票 V{ApplicationVersion.Read()}（C# 重製測試版）";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 850);
        ClientSize = new Size(1164, 811);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.FromArgb(245, 245, 245);

        repository = LocalRepository.Open(AppContext.BaseDirectory, new DpapiSecretProtector());
        service = new InvoiceService(repository);
        recordsPage = new RecordsControl(repository, service);
        invoicePage = new InvoiceEntryControl(repository, service, recordsPage.Reload);
        BuildShell();
        UpdateEnvironment();
        Shown += async (_, _) =>
        {
            if (!EnsureInitialSetup()) return;
            UpdateEnvironment();
            invoicePage.RefreshEnvironment();
            await RefreshApiAsync();
        };
    }

    private void BuildShell()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(20, 16, 20, 16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var banner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 246, 255) };
        environmentLabel.Dock = DockStyle.Fill;
        environmentLabel.TextAlign = ContentAlignment.MiddleCenter;
        environmentLabel.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        environmentLabel.ForeColor = Color.FromArgb(0, 72, 170);
        apiLabel.Dock = DockStyle.Right;
        apiLabel.Width = 170;
        apiLabel.TextAlign = ContentAlignment.MiddleRight;
        apiLabel.Padding = new Padding(0, 0, 14, 0);
        banner.Controls.Add(environmentLabel);
        banner.Controls.Add(apiLabel);

        var tabHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        tabs.Dock = DockStyle.Fill;
        tabs.Appearance = TabAppearance.Normal;
        tabs.DrawMode = TabDrawMode.Normal;
        tabs.Multiline = false;
        tabs.Padding = new Point(14, 5);
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
        settingsButton.Width = 120;
        settingsButton.Height = 30;
        settingsButton.Margin = Padding.Empty;
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        settingsButton.ForeColor = SystemColors.ControlText;
        settingsButton.UseVisualStyleBackColor = true;
        settingsButton.Click += (_, _) => OpenSettings();
        tabHost.Controls.Add(tabs);
        tabHost.Controls.Add(settingsButton);
        tabHost.Resize += (_, _) => PositionSettingsButton(tabHost);
        PositionSettingsButton(tabHost);
        settingsButton.BringToFront();
        root.Controls.Add(banner, 0, 0);
        root.Controls.Add(tabHost, 0, 1);
        Controls.Add(root);
    }

    private void PositionSettingsButton(Control tabHost)
    {
        settingsButton.Location = new Point(Math.Max(0, tabHost.ClientSize.Width - settingsButton.Width - 8), 2);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(repository);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        UpdateEnvironment();
        invoicePage.RefreshEnvironment();
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
        invoicePage.VerifySmokeLayout();
        recordsPage.VerifySmokeLayout();
        using var firstSetup = new InitialSetupForm(repository);
        firstSetup.CreateControl();
        firstSetup.PerformLayout();
        firstSetup.VerifySmokeLayout();
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
