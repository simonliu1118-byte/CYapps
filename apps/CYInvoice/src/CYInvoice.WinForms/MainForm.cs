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
    private readonly Button invoiceTab = new();
    private readonly Button recordsTab = new();
    private readonly Panel invoiceLine = new();
    private readonly Panel recordsLine = new();
    private readonly Panel content = new();
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
        ShowPage(invoicePage);
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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20, 16, 20, 16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
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

        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        var tabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
        };
        ConfigureTab(invoiceTab, invoiceLine, "開立發票", () => ShowPage(invoicePage));
        ConfigureTab(recordsTab, recordsLine, "已開立發票清單", () => { recordsPage.Reload(); ShowPage(recordsPage); });
        tabs.Controls.Add(TabContainer(invoiceTab, invoiceLine, 94));
        tabs.Controls.Add(TabContainer(recordsTab, recordsLine, 142));
        var settingsHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 6),
        };
        settingsButton.Text = "設定";
        settingsButton.Width = 120;
        settingsButton.Height = 34;
        settingsButton.Margin = Padding.Empty;
        settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        settingsButton.ForeColor = SystemColors.ControlText;
        settingsButton.UseVisualStyleBackColor = true;
        settingsButton.Click += (_, _) => OpenSettings();
        settingsHost.Controls.Add(settingsButton);
        navigation.Controls.Add(tabs, 0, 0);
        navigation.Controls.Add(settingsHost, 1, 0);

        content.Dock = DockStyle.Fill;
        content.BackColor = Color.White;
        content.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(banner, 0, 0);
        root.Controls.Add(navigation, 0, 1);
        root.Controls.Add(content, 0, 2);
        Controls.Add(root);
    }

    private void ConfigureTab(Button button, Panel line, string text, Action action)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 246, 253);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(229, 240, 252);
        button.UseVisualStyleBackColor = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Click += (_, _) => action();
        line.Dock = DockStyle.Bottom;
        line.Height = 3;
        line.BackColor = Color.FromArgb(0, 102, 204);
    }

    private static Panel TabContainer(Button button, Panel line, int width)
    {
        var panel = new Panel { Width = width, Height = 44, Margin = new Padding(0, 0, 2, 0) };
        panel.Controls.Add(button);
        panel.Controls.Add(line);
        line.BringToFront();
        return panel;
    }

    private void ShowPage(Control page)
    {
        foreach (Control control in content.Controls) control.Visible = ReferenceEquals(control, page);
        if (!content.Controls.Contains(page))
        {
            page.Dock = DockStyle.Fill;
            content.Controls.Add(page);
        }
        page.Visible = true;
        page.BringToFront();
        var invoiceSelected = ReferenceEquals(page, invoicePage);
        SetTabAppearance(invoiceTab, invoiceLine, invoiceSelected);
        SetTabAppearance(recordsTab, recordsLine, !invoiceSelected);
    }

    private void SetTabAppearance(Button button, Panel line, bool selected)
    {
        button.ForeColor = selected ? Color.FromArgb(0, 82, 180) : SystemColors.ControlText;
        button.BackColor = selected ? Color.FromArgb(235, 243, 252) : Color.FromArgb(248, 248, 248);
        button.FlatAppearance.BorderColor = selected ? Color.FromArgb(180, 202, 226) : Color.FromArgb(218, 218, 218);
        button.Font = new Font(Font, selected ? FontStyle.Bold : FontStyle.Regular);
        line.Visible = true;
        line.Height = selected ? 3 : 1;
        line.BackColor = selected ? Color.FromArgb(0, 102, 204) : Color.FromArgb(205, 205, 205);
        line.BringToFront();
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
        if (settings.AdminPasswordSet && settings.MoPasswordEncrypted.Length != 0) return true;
        MessageBox.Show(this,
            "首次使用必須先建立設定管理密碼與 MO店+ Excel 保護密碼，完成前不會開放發票操作。",
            "首次安全設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var form = new SettingsForm(repository);
        if (form.ShowDialog(this) == DialogResult.OK) return true;
        Close();
        return false;
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
            return value.Length == 0 ? "1.1.0-cs.3" : value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "1.1.0-cs.3";
        }
    }
}
