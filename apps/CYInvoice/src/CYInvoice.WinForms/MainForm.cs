using CYInvoice.Core;
using CYInvoice.Core.Amego;
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
    private const int SyncIntervalMilliseconds = 5 * 60 * 1000;
    private const int HeaderButtonGap = 6;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly InvoiceSyncCoordinator syncCoordinator;
    private readonly CancellationTokenSource syncLifetime = new();
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = SyncIntervalMilliseconds };
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
    private readonly Button forgotPasswordButton = UiControls.StandardButton("忘記密碼");
    private readonly Button accountManagementButton = UiControls.StandardButton("帳戶管理");
    private readonly Button settingsButton = UiControls.StandardButton("設定");
    private readonly Label copyrightLabel = new()
    {
        Text = "Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.",
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        ForeColor = Color.FromArgb(128, 128, 128),
        TextAlign = ContentAlignment.MiddleRight,
        Margin = new Padding(0, 0, 6, 0),
    };
    private bool shuttingDown;

    public MainForm(bool startupSmokeTest = false)
    {
        Text = $"CY 電子發票 V{ApplicationVersion.Read()}";
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
        var syncService = new InvoiceSyncService(repository);
        var automaticSyncService = new InvoiceAutomaticSyncService(repository, syncService);
        syncCoordinator = new InvoiceSyncCoordinator(syncService, automaticService: automaticSyncService);
        recordsPage = new RecordsControl(repository, service, syncCoordinator, syncLifetime.Token);
        invoicePage = new InvoiceEntryControl(repository, service, recordsPage.Reload);
        syncTimer.Tick += async (_, _) => await RunScheduledSyncAsync();
        FormClosing += (_, _) => StopBackgroundSync();
        BuildShell();
        UpdateEnvironment();
        if (!startupSmokeTest) Shown += async (_, _) =>
        {
            if (!EnsureInitialSetup()) return;
            UpdateEnvironment();
            invoicePage.RefreshEnvironment();
            await RefreshApiAsync();
            if (shuttingDown) return;
            await RunStartupSyncAsync();
            if (!shuttingDown) syncTimer.Start();
        };
    }

    private void BuildShell()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20, 2, 20, 2) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        copyrightLabel.Font = new Font(Font.FontFamily, 8F);

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

        ConfigureHeaderButton(forgotPasswordButton, OpenPasswordRecovery);
        ConfigureHeaderButton(accountManagementButton, OpenAccountManagement);
        ConfigureHeaderButton(settingsButton, OpenSettings);
        tabHost.Controls.Add(tabs);
        tabHost.Controls.Add(forgotPasswordButton);
        tabHost.Controls.Add(accountManagementButton);
        tabHost.Controls.Add(settingsButton);
        tabHost.Resize += (_, _) => PositionHeaderButtons();
        tabHost.Layout += (_, _) => PositionHeaderButtons();
        root.Controls.Add(banner, 0, 0);
        root.Controls.Add(tabHost, 0, 1);
        root.Controls.Add(copyrightLabel, 0, 2);
        Controls.Add(root);
        PositionHeaderButtons();
    }

    private static void ConfigureHeaderButton(Button button, Action action)
    {
        button.Margin = Padding.Empty;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.ForeColor = SystemColors.ControlText;
        button.UseVisualStyleBackColor = true;
        button.Click += (_, _) => action();
    }

    private void PositionHeaderButtons()
    {
        var headerHeight = settingsButton.Height;
        if (tabs.IsHandleCreated && tabs.TabCount > 0)
            headerHeight = Math.Max(settingsButton.Height, tabs.GetTabRect(0).Height);
        var top = Math.Max(0, (headerHeight - settingsButton.Height) / 2);
        var right = Math.Max(0, tabHost.ClientSize.Width - settingsButton.Width - HeaderButtonGap);
        settingsButton.SetBounds(right, top, settingsButton.Width, settingsButton.Height);
        accountManagementButton.SetBounds(
            Math.Max(0, settingsButton.Left - accountManagementButton.Width - HeaderButtonGap),
            top,
            accountManagementButton.Width,
            accountManagementButton.Height);
        forgotPasswordButton.SetBounds(
            Math.Max(0, accountManagementButton.Left - forgotPasswordButton.Width - HeaderButtonGap),
            top,
            forgotPasswordButton.Width,
            forgotPasswordButton.Height);
        forgotPasswordButton.BringToFront();
        accountManagementButton.BringToFront();
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
        if (!TryAuthenticateAdministrator("開啟設定", out _)) return;
        using var form = new SettingsForm(repository);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        UpdateEnvironment();
        invoicePage.RefreshEnvironment();
        recordsPage.Reload();
        _ = RefreshApiAsync();
    }

    private void OpenAccountManagement()
    {
        if (!TryAuthenticateAdministrator("帳戶管理驗證", out var account)) return;
        using var form = new AccountManagementForm(repository.Employees, account!);
        form.ShowDialog(this);
    }

    private void OpenPasswordRecovery()
    {
        if (!repository.Employees.HasEmployees())
        {
            MessageBox.Show(this, "尚未建立員工帳戶，請先完成首次設定。", "無法復原密碼",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var form = new SuperAdminRecoveryForm(repository.Employees);
        form.ShowDialog(this);
    }

    private bool TryAuthenticateAdministrator(string title, out EmployeeAccount? account)
    {
        account = null;
        if (!repository.Employees.HasEmployees())
        {
            MessageBox.Show(this, "尚未建立員工帳戶，請先完成首次設定。", title,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        using var login = new EmployeeAdminLoginForm(repository.Employees, title);
        if (login.ShowDialog(this) != DialogResult.OK || login.AuthenticatedEmployee is null) return false;
        account = login.AuthenticatedEmployee;
        return true;
    }

    private bool EnsureInitialSetup()
    {
        if (!repository.Employees.HasEmployees())
        {
            using var setup = new InitialSetupForm(repository);
            if (setup.ShowDialog(this) != DialogResult.OK)
            {
                Close();
                return false;
            }
        }

        var settings = repository.Settings.LoadOrCreate();
        if (MoPasswordReady(settings)) return true;
        MessageBox.Show(this,
            "MO店+ Excel 密碼尚未設定，或無法在目前 Windows 帳號解密。請由管理員重新輸入。",
            "需要補齊設定",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        if (!TryAuthenticateAdministrator("補齊設定驗證", out _))
        {
            Close();
            return false;
        }
        using var form = new SettingsForm(repository, requireMoPassword: true);
        if (form.ShowDialog(this) == DialogResult.OK && MoPasswordReady(repository.Settings.LoadOrCreate())) return true;
        Close();
        return false;
    }

    private bool MoPasswordReady(Settings settings)
    {
        if (settings.MoPasswordEncrypted.Length == 0) return false;
        try
        {
            return !string.IsNullOrWhiteSpace(repository.Settings.MoPassword(settings));
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal void VerifySmokeLayout()
    {
        if (tabs.TabPages.Count != 2 || tabs.TabPages[0] != invoiceTab || tabs.TabPages[1] != recordsTab ||
            tabs.DrawMode != TabDrawMode.Normal || tabs.TabStop)
            throw new InvalidOperationException("主頁籤未使用無焦點虛線的原生 TabControl");
        if (Math.Abs(Font.SizeInPoints - 12F) > 0.1F ||
            Math.Abs(environmentCompanyLabel.Font.SizeInPoints - 14F) > 0.1F)
            throw new InvalidOperationException("主畫面與環境標題字級不正確");
        if (copyrightLabel.Text != "Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved." ||
            Math.Abs(copyrightLabel.Font.SizeInPoints - 8F) > 0.1F ||
            copyrightLabel.Parent is null)
            throw new InvalidOperationException("主畫面 Copyright footer 文字或樣式不正確");
        if (EnvironmentCompanyText(Environments.Test, AmegoDefaults.TestInvoice, string.Empty) != "光貿測試公司 12345678" ||
            EnvironmentCompanyText(Environments.Production, "12345675", "志遠醫療器材行") != "志遠醫療器材行 12345675" ||
            EnvironmentCompanyText(Environments.Production, "12345675", string.Empty) != "公司名稱查詢中 12345675" ||
            EnvironmentCompanyText(Environments.Production, "12345675", string.Empty, lookupCompleted: true) != "公司名稱查無資料 12345675" ||
            EnvironmentCompanyText(Environments.Production, "12345675", string.Empty, lookupFailed: true) != "公司名稱查詢失敗 12345675")
            throw new InvalidOperationException("測試／正式環境公司標題文字不正確");
        if (syncTimer.Interval != SyncIntervalMilliseconds || syncTimer.Enabled)
            throw new InvalidOperationException("背景同步 Timer 未維持 5 分鐘且 startup smoke 不應自動啟動");
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
        PositionHeaderButtons();
        var tabHeader = tabs.GetTabRect(0);
        if (settingsButton.Right != tabHost.ClientSize.Width - HeaderButtonGap ||
            accountManagementButton.Right + HeaderButtonGap != settingsButton.Left ||
            forgotPasswordButton.Right + HeaderButtonGap != accountManagementButton.Left ||
            Math.Abs((settingsButton.Top + settingsButton.Height / 2) - (tabHeader.Top + tabHeader.Height / 2)) > 2 ||
            !UiControls.HasLogicalSize(settingsButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(accountManagementButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(forgotPasswordButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("主畫面帳戶／復原／設定按鈕未以標準尺寸對齊頁籤標頭右側");
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

        using var employeeLogin = new EmployeeAdminLoginForm(repository.Employees);
        employeeLogin.Show(this);
        employeeLogin.PerformLayout();
        Application.DoEvents();
        employeeLogin.VerifySmokeLayout();
        employeeLogin.Close();

        using var settings = new SettingsForm(repository);
        settings.Show(this);
        settings.PerformLayout();
        Application.DoEvents();
        settings.VerifySmokeLayout();
        settings.Close();

        var now = DateTimeOffset.UtcNow;
        var smokeAdmin = new EmployeeAccount("0000", "測試超管", string.Empty, EmployeeRoles.SuperAdmin, true, now, now);
        using var management = new AccountManagementForm(repository.Employees, smokeAdmin);
        management.Show(this);
        management.PerformLayout();
        Application.DoEvents();
        management.VerifySmokeLayout();
        management.Close();

        using var employeeEdit = new EmployeeEditForm();
        employeeEdit.Show(this);
        employeeEdit.PerformLayout();
        Application.DoEvents();
        employeeEdit.VerifySmokeLayout();
        employeeEdit.Close();

        using var passwordReset = new EmployeePasswordResetForm("0001", "測試員工");
        passwordReset.Show(this);
        passwordReset.PerformLayout();
        Application.DoEvents();
        passwordReset.VerifySmokeLayout();
        passwordReset.Close();

        using var passwordChange = new EmployeeChangePasswordForm(repository.Employees, smokeAdmin);
        passwordChange.Show(this);
        passwordChange.PerformLayout();
        Application.DoEvents();
        passwordChange.VerifySmokeLayout();
        passwordChange.Close();

        using var rotateRecovery = new RotateRecoveryCodeForm(repository.Employees, smokeAdmin);
        rotateRecovery.Show(this);
        rotateRecovery.PerformLayout();
        Application.DoEvents();
        rotateRecovery.VerifySmokeLayout();
        rotateRecovery.Close();

        using var recoveryCode = new RecoveryCodeForm("CYR-2345-6789-ABCD-EFGH-JKLM");
        recoveryCode.Show(this);
        recoveryCode.PerformLayout();
        Application.DoEvents();
        recoveryCode.VerifySmokeLayout();
        recoveryCode.Close();

        using var recovery = new SuperAdminRecoveryForm(repository.Employees);
        recovery.Show(this);
        recovery.PerformLayout();
        Application.DoEvents();
        recovery.VerifySmokeLayout();
        recovery.Close();

        ImportConfirmationForm.VerifySmokeLayout(repository, service);
        RecordDetailForm.VerifySmokeLayout(repository, service);
    }

    private void UpdateEnvironment()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (settings.Environment == Environments.Production)
        {
            environmentBadgeLabel.Text = "正式環境｜將開立正式發票";
            environmentBadgeLabel.BackColor = Color.FromArgb(255, 238, 238);
            environmentBadgeLabel.ForeColor = Color.FromArgb(166, 32, 32);
            environmentCompanyLabel.Text = EnvironmentCompanyText(settings.Environment, settings.ProductionInvoice, string.Empty);
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
        var requestedSettings = repository.Settings.LoadOrCreate();
        var requestedEnvironment = requestedSettings.Environment;
        var requestedInvoice = requestedEnvironment == Environments.Production
            ? requestedSettings.ProductionInvoice.Trim()
            : AmegoDefaults.TestInvoice;
        apiLabel.Text = "● API 檢查中";
        apiLabel.ForeColor = Color.FromArgb(196, 126, 0);
        try
        {
            var companyName = await service.HealthCheckAsync();
            if (!CurrentEnvironmentMatches(requestedEnvironment, requestedInvoice)) return;
            environmentCompanyLabel.Text = EnvironmentCompanyText(requestedEnvironment, requestedInvoice, companyName, lookupCompleted: true);
            apiLabel.Text = "● API 正常";
            apiLabel.ForeColor = Color.FromArgb(0, 155, 72);
        }
        catch (Exception error)
        {
            if (!CurrentEnvironmentMatches(requestedEnvironment, requestedInvoice)) return;
            environmentCompanyLabel.Text = EnvironmentCompanyText(requestedEnvironment, requestedInvoice, string.Empty, lookupFailed: true);
            apiLabel.Text = "● API 異常";
            apiLabel.ForeColor = Color.FromArgb(196, 0, 0);
            apiLabel.AccessibleDescription = error.Message;
        }
    }

    private async Task RunStartupSyncAsync()
    {
        try
        {
            var run = await syncCoordinator.RunStartupAsync(syncLifetime.Token);
            if (run.Status == InvoiceSyncRunStatus.Completed && !shuttingDown) recordsPage.Reload();
            if (run.Result is { Problems.Count: > 0 })
                apiLabel.AccessibleDescription = $"啟動同步有 {run.Result.Problems.Count} 項未完整更新";
        }
        catch (OperationCanceledException) when (syncLifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!shuttingDown) apiLabel.AccessibleDescription = "啟動同步失敗：" + error.Message;
        }
    }

    private async Task RunScheduledSyncAsync()
    {
        try
        {
            var run = await syncCoordinator.RunScheduledAsync(syncLifetime.Token);
            if (run.Status == InvoiceSyncRunStatus.Completed && !shuttingDown) recordsPage.Reload();
            if (run.Result is { Problems.Count: > 0 })
                apiLabel.AccessibleDescription = $"背景同步有 {run.Result.Problems.Count} 項未完整更新";
        }
        catch (OperationCanceledException) when (syncLifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!shuttingDown) apiLabel.AccessibleDescription = "背景同步失敗：" + error.Message;
        }
    }

    private void StopBackgroundSync()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        syncTimer.Stop();
        syncLifetime.Cancel();
    }

    private bool CurrentEnvironmentMatches(string environment, string invoice)
    {
        var current = repository.Settings.LoadOrCreate();
        var currentInvoice = current.Environment == Environments.Production
            ? current.ProductionInvoice.Trim()
            : AmegoDefaults.TestInvoice;
        return current.Environment == environment && currentInvoice == invoice;
    }

    private static string EnvironmentCompanyText(
        string environment,
        string invoice,
        string companyName,
        bool lookupCompleted = false,
        bool lookupFailed = false)
    {
        if (environment != Environments.Production) return "光貿測試公司 12345678";
        companyName = companyName.Trim();
        if (companyName.Length != 0) return $"{companyName} {invoice.Trim()}";
        var status = lookupFailed ? "公司名稱查詢失敗" : lookupCompleted ? "公司名稱查無資料" : "公司名稱查詢中";
        return $"{status} {invoice.Trim()}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopBackgroundSync();
            syncTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class ApplicationVersion
{
    public static string Read()
    {
        try
        {
            var value = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "VERSION")).Trim();
            return value.Length == 0 ? "2.0.0" : value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "2.0.0";
        }
    }
}
