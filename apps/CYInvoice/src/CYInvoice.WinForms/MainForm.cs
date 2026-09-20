using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;
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
    private const int EnvironmentTagWidth = 210;
    private const int EnvironmentTagHeight = 34;
    private const int BannerSideWidth = 450;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly InvoiceSyncCoordinator syncCoordinator;
    private readonly AmegoConnectivityProbe connectivityProbe = new();
    private readonly HttpClient cloudHealthHttpClient = new();
    private readonly CancellationTokenSource syncLifetime = new();
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = SyncIntervalMilliseconds };
    private readonly InvoiceEntryControl invoicePage;
    private readonly RecordsControl recordsPage;
    private readonly Panel banner = new();
    private readonly TableLayoutPanel bannerLayout = new();
    private readonly FlowLayoutPanel environmentTags = new();
    private readonly Label environmentBadgeLabel = new();
    private readonly Label environmentWarningLabel = new();
    private readonly Label environmentCompanyLabel = new();
    private readonly TableLayoutPanel runtimeStatusLayout = new();
    private readonly Label runtimeModeLabel = new();
    private readonly Label apiLabel = new();
    private readonly ToolTip apiToolTip = new();
    private readonly ToolTip runtimeModeToolTip = new();
    private readonly ToolTip environmentToolTip = new();
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
        SetAmegoConnectionState("啟動中", Color.FromArgb(128, 128, 128), "首次設定完成後將檢查光貿 API 連線。");
        UpdateEnvironment();
        if (!startupSmokeTest) Shown += async (_, _) =>
        {
            if (!EnsureInitialSetup()) return;
            UpdateEnvironment();
            invoicePage.RefreshEnvironment();
            await Task.WhenAll(RefreshRuntimeModeAsync(), RefreshApiAsync());
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
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BannerSideWidth));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BannerSideWidth));
        bannerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        environmentTags.Dock = DockStyle.Fill;
        environmentTags.FlowDirection = FlowDirection.LeftToRight;
        environmentTags.WrapContents = false;
        environmentTags.Margin = Padding.Empty;
        environmentTags.Padding = new Padding(12, 6, 0, 0);
        ConfigureEnvironmentTag(environmentBadgeLabel);
        ConfigureEnvironmentTag(environmentWarningLabel);
        environmentBadgeLabel.Margin = Padding.Empty;
        environmentWarningLabel.Margin = new Padding(6, 0, 0, 0);
        environmentWarningLabel.Text = "⚠ 正式設定異常";
        environmentWarningLabel.BackColor = Color.FromArgb(255, 245, 210);
        environmentWarningLabel.ForeColor = Color.FromArgb(166, 92, 0);
        environmentWarningLabel.Visible = false;
        environmentTags.Controls.Add(environmentBadgeLabel);
        environmentTags.Controls.Add(environmentWarningLabel);

        environmentCompanyLabel.Dock = DockStyle.Fill;
        environmentCompanyLabel.Margin = Padding.Empty;
        environmentCompanyLabel.TextAlign = ContentAlignment.MiddleCenter;
        environmentCompanyLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        environmentCompanyLabel.ForeColor = Color.FromArgb(0, 72, 170);
        environmentCompanyLabel.AutoEllipsis = false;

        runtimeStatusLayout.Dock = DockStyle.Fill;
        runtimeStatusLayout.Margin = Padding.Empty;
        runtimeStatusLayout.Padding = new Padding(0, 2, 14, 2);
        runtimeStatusLayout.ColumnCount = 2;
        runtimeStatusLayout.RowCount = 2;
        ConfigureRuntimeText(runtimeModeLabel);
        ConfigureRuntimeText(apiLabel);
        runtimeStatusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runtimeStatusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, RuntimeStatusPreferredWidth()));
        runtimeStatusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        runtimeStatusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        runtimeStatusLayout.Controls.Add(runtimeModeLabel, 1, 0);
        runtimeStatusLayout.Controls.Add(apiLabel, 1, 1);

        bannerLayout.Controls.Add(environmentTags, 0, 0);
        bannerLayout.Controls.Add(environmentCompanyLabel, 1, 0);
        bannerLayout.Controls.Add(runtimeStatusLayout, 2, 0);
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

    private void ConfigureEnvironmentTag(Label label)
    {
        label.AutoSize = false;
        label.Size = new Size(EnvironmentTagWidth, EnvironmentTagHeight);
        label.BorderStyle = BorderStyle.FixedSingle;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
    }

    private void ConfigureRuntimeText(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.BorderStyle = BorderStyle.None;
        label.BackColor = Color.Transparent;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        label.Margin = new Padding(0, 1, 0, 1);
    }

    private int RuntimeStatusPreferredWidth()
    {
        var runtimeWidth = TextRenderer.MeasureText("雲端-單機運行", runtimeModeLabel.Font).Width;
        var amegoWidth = TextRenderer.MeasureText("光貿連線異常", apiLabel.Font).Width;
        return Math.Max(runtimeWidth, amegoWidth) + 8;
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
        _ = RefreshRuntimeModeAsync();
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
        if (repository.Employees.HasEmployees()) return true;
        using var setup = new InitialSetupForm(repository);
        if (setup.ShowDialog(this) == DialogResult.OK) return true;
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
            bannerLayout.GetColumn(environmentTags) != 0 ||
            bannerLayout.GetColumn(environmentCompanyLabel) != 1 ||
            bannerLayout.GetColumn(runtimeStatusLayout) != 2 ||
            runtimeStatusLayout.GetColumn(runtimeModeLabel) != 1 ||
            runtimeStatusLayout.GetColumn(apiLabel) != 1 ||
            environmentBadgeLabel.Width != environmentWarningLabel.Width ||
            environmentBadgeLabel.Width != EnvironmentTagWidth ||
            runtimeModeLabel.Width != apiLabel.Width ||
            runtimeModeLabel.Width != RuntimeStatusPreferredWidth() ||
            runtimeModeLabel.BorderStyle != BorderStyle.None || apiLabel.BorderStyle != BorderStyle.None ||
            runtimeModeLabel.Text != "單機模式" || apiLabel.Text != "啟動中")
            throw new InvalidOperationException("標題列環境標籤、執行模式、連線狀態或公司名稱未依指定方式排列");
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
        UpdateRuntimeModeInitial(settings);
        if (settings.Environment == Environments.Production)
        {
            environmentBadgeLabel.Text = "正式環境｜將開立正式發票";
            environmentBadgeLabel.BackColor = Color.FromArgb(255, 238, 238);
            environmentBadgeLabel.ForeColor = Color.FromArgb(166, 32, 32);
            var problem = ProductionConfigurationProblem(settings);
            SetProductionWarning(problem);
            environmentCompanyLabel.Text = problem.Length == 0
                ? EnvironmentCompanyText(settings.Environment, settings.ProductionInvoice, string.Empty)
                : "正式公司設定未完成";
        }
        else
        {
            environmentBadgeLabel.Text = "測試環境｜不會開立正式發票";
            environmentBadgeLabel.BackColor = Color.FromArgb(255, 247, 221);
            environmentBadgeLabel.ForeColor = Color.FromArgb(166, 92, 0);
            SetProductionWarning(string.Empty);
            environmentCompanyLabel.Text = "光貿測試公司 12345678";
        }
    }

    private void UpdateRuntimeModeInitial(Settings settings)
    {
        if (settings.CloudMode == CloudModes.LocalOnly)
        {
            SetRuntimeModeState(
                "單機模式",
                SystemColors.ControlText,
                "CYInvoice 目前使用單機模式，不會呼叫 CYInvoice Cloud API。");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.CloudBaseUrl))
        {
            SetRuntimeModeState(
                "雲端-單機運行",
                Color.FromArgb(180, 0, 0),
                "已選擇雲端模式，但尚未設定 CYInvoice Cloud API 網址。\n目前以單機方式運行。",
                showToolTip: true);
            return;
        }

        SetRuntimeModeState(
            "雲端模式",
            SystemColors.ControlText,
            "已選擇雲端模式，正在確認 CYInvoice Cloud API 狀態。");
    }

    private async Task RefreshRuntimeModeAsync()
    {
        var requested = repository.Settings.LoadOrCreate();
        var requestedMode = requested.CloudMode;
        var requestedUrl = requested.CloudBaseUrl.Trim();

        if (requestedMode == CloudModes.LocalOnly)
        {
            UpdateRuntimeModeInitial(requested);
            return;
        }

        if (requestedUrl.Length == 0)
        {
            UpdateRuntimeModeInitial(requested);
            return;
        }

        try
        {
            var client = new CloudClient(cloudHealthHttpClient, new Uri(requestedUrl, UriKind.Absolute));
            var health = await client.CheckHealthAsync(syncLifetime.Token);
            if (!CurrentCloudSettingsMatch(requestedMode, requestedUrl)) return;
            var problem = CloudCompatibility.Problem(health);
            if (problem.Length != 0)
            {
                SetRuntimeModeState(
                    "雲端-單機運行",
                    Color.FromArgb(180, 0, 0),
                    "CYInvoice Cloud API 目前無法使用，已改以單機方式運行。\n\n" + problem,
                    showToolTip: true);
                return;
            }

            var onboarding = await client.GetOnboardingStatusAsync(syncLifetime.Token);
            if (!CurrentCloudSettingsMatch(requestedMode, requestedUrl)) return;
            var onboardingText = onboarding.WorkspaceInitialized ? "已建立雲端空間" : "尚未建立雲端空間";
            SetRuntimeModeState(
                "雲端模式",
                SystemColors.ControlText,
                $"CYInvoice Cloud API 連線正常。\n{CloudCompatibility.SuccessSummary(health)}\n{onboardingText}");
        }
        catch (OperationCanceledException) when (syncLifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!CurrentCloudSettingsMatch(requestedMode, requestedUrl)) return;
            SetRuntimeModeState(
                "雲端-單機運行",
                Color.FromArgb(180, 0, 0),
                "CYInvoice Cloud API 目前無法使用，已改以單機方式運行。\n\n" + ExceptionDetails(error),
                showToolTip: true);
        }
    }

    private void SetRuntimeModeState(string text, Color foreground, string details, bool showToolTip = false)
    {
        runtimeModeLabel.Text = text;
        runtimeModeLabel.BackColor = Color.Transparent;
        runtimeModeLabel.ForeColor = foreground;
        runtimeModeLabel.AccessibleDescription = details;
        runtimeModeToolTip.SetToolTip(runtimeModeLabel, showToolTip ? details : string.Empty);
    }

    private async Task RefreshApiAsync()
    {
        var requestedSettings = repository.Settings.LoadOrCreate();
        var requestedEnvironment = requestedSettings.Environment;
        var requestedInvoice = requestedEnvironment == Environments.Production
            ? requestedSettings.ProductionInvoice.Trim()
            : AmegoDefaults.TestInvoice;
        SetAmegoConnectionState(
            "光貿連線中",
            Color.FromArgb(128, 128, 128),
            "正在檢查光貿 API 服務連線");

        try
        {
            await connectivityProbe.CheckAsync(syncLifetime.Token);
        }
        catch (OperationCanceledException) when (syncLifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            if (!CurrentEnvironmentMatches(requestedEnvironment, requestedInvoice)) return;
            var details = ExceptionDetails(error);
            SetAmegoConnectionState(
                "光貿連線異常",
                Color.FromArgb(180, 0, 0),
                "光貿 API 連線異常\n" + details,
                showToolTip: true);
            return;
        }

        if (!CurrentEnvironmentMatches(requestedEnvironment, requestedInvoice)) return;
        SetAmegoConnectionState(
            "光貿連線正常",
            Color.FromArgb(0, 120, 60),
            "光貿 API 服務連線正常");

        if (requestedEnvironment != Environments.Production)
        {
            environmentCompanyLabel.Text = "光貿測試公司 12345678";
            return;
        }

        var configurationProblem = ProductionConfigurationProblem(requestedSettings);
        if (configurationProblem.Length != 0)
        {
            SetProductionWarning(configurationProblem);
            environmentCompanyLabel.Text = "正式公司設定未完成";
            return;
        }

        try
        {
            var companyName = await service.HealthCheckAsync(syncLifetime.Token);
            if (!CurrentEnvironmentMatches(requestedEnvironment, requestedInvoice)) return;
            environmentCompanyLabel.Text = EnvironmentCompanyText(
                requestedEnvironment,
                requestedInvoice,
                companyName,
                lookupCompleted: true);
            SetProductionWarning(companyName.Trim().Length == 0
                ? "公司統編查詢完成，但光貿未回傳公司名稱。請確認正式環境統編與 App Key。"
                : string.Empty);
        }
        catch (OperationCanceledException) when (syncLifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!CurrentEnvironmentMatches(requestedEnvironment, requestedInvoice)) return;
            environmentCompanyLabel.Text = EnvironmentCompanyText(
                requestedEnvironment,
                requestedInvoice,
                string.Empty,
                lookupFailed: true);
            SetProductionWarning("正式公司資料查詢失敗。\n" + ExceptionDetails(error));
        }
    }

    private void SetAmegoConnectionState(string text, Color foreground, string details, bool showToolTip = false)
    {
        apiLabel.Text = text;
        apiLabel.BackColor = Color.Transparent;
        apiLabel.ForeColor = foreground;
        apiLabel.AccessibleDescription = details;
        apiToolTip.SetToolTip(apiLabel, showToolTip ? details : string.Empty);
    }

    private string ProductionConfigurationProblem(Settings settings)
    {
        if (settings.Environment != Environments.Production) return string.Empty;
        var invoice = settings.ProductionInvoice.Trim();
        if (invoice.Length != 8 || !invoice.All(character => character is >= '0' and <= '9'))
            return "正式公司統編尚未設定或不是 8 碼數字。";
        if (settings.ProductionAppKeyEncrypted.Length == 0)
            return "正式環境尚未設定 App Key。";
        try
        {
            if (string.IsNullOrWhiteSpace(repository.Settings.ProductionAppKey(settings)))
                return "正式環境 App Key 為空白。";
        }
        catch (Exception error)
        {
            return "正式環境 App Key 無法解密。\n" + ExceptionDetails(error);
        }
        return string.Empty;
    }

    private void SetProductionWarning(string details)
    {
        details = details.Trim();
        environmentWarningLabel.Visible = details.Length != 0;
        environmentWarningLabel.AccessibleDescription = details;
        environmentToolTip.SetToolTip(environmentWarningLabel, details);
    }

    private static string ExceptionDetails(Exception error)
    {
        var lines = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            var text = current.Message.Trim();
            if (text.Length != 0 && !lines.Contains(text, StringComparer.Ordinal)) lines.Add(text);
        }
        return lines.Count == 0 ? error.GetType().Name : string.Join(Environment.NewLine, lines);
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
        _ = RefreshRuntimeModeAsync();
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

    private bool CurrentCloudSettingsMatch(string mode, string baseUrl)
    {
        var current = repository.Settings.LoadOrCreate();
        return current.CloudMode == mode
            && string.Equals(current.CloudBaseUrl.Trim(), baseUrl, StringComparison.OrdinalIgnoreCase);
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
            cloudHealthHttpClient.Dispose();
            apiToolTip.Dispose();
            runtimeModeToolTip.Dispose();
            environmentToolTip.Dispose();
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
