using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudSetupForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox baseUrl = UiControls.TextBox(240);
    private readonly Label status = UiControls.Label(string.Empty);
    private readonly ToolTip toolTip = new();
    private readonly Button check = UiControls.StandardButton("測試連線");
    private readonly Button save = UiControls.StandardButton("儲存");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private readonly string initialBaseUrl;
    private bool busy;
    private bool stateError;
    private bool resourcesDisposed;
    private string confirmedBaseUrl = string.Empty;
    private string confirmedSummary = string.Empty;

    public CloudSetupForm(LocalRepository repository, string? initialBaseUrl = null, Settings? liveSettings = null)
    {
        this.repository = repository;
        settings = liveSettings ?? repository.Settings.LoadOrCreate();
        this.initialBaseUrl = initialBaseUrl ?? settings.CloudBaseUrl;
        SelectedBaseUrl = this.initialBaseUrl;
        Text = "雲端連線設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 196);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        baseUrl.PlaceholderText = "https://cloud.example.com/";

        BuildLayout();
        // CYID Device enrollment and disaster recovery use CloudDirectJoinForm.
        // An enrolled Device may inspect but must not edit its bound endpoint.
        baseUrl.ReadOnly = HasCloudIdentity(settings);
        LoadValues();
        baseUrl.TextChanged += (_, _) =>
        {
            confirmedBaseUrl = string.Empty;
            confirmedSummary = string.Empty;
            SelectedOnboardingStatus = null;
            UpdateState();
        };
        UpdateState();
    }

    public string SelectedBaseUrl { get; private set; }
    public CloudOnboardingStatus? SelectedOnboardingStatus { get; private set; }
    public bool IdentityCompleted { get; private set; }

    private void BuildLayout()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var cloudGroup = new GroupBox { Text = "CYInvoice Cloud API", Dock = DockStyle.Fill };
        var cloud = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(8, 6, 8, 6),
            Margin = Padding.Empty,
        };
        cloud.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        cloud.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cloud.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        cloud.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        cloud.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cloud.Controls.Add(UiControls.Label("API 網址"), 0, 0);
        cloud.Controls.Add(baseUrl, 1, 0);
        cloud.SetColumnSpan(baseUrl, 2);
        cloud.Controls.Add(UiControls.Label("狀態"), 0, 1);
        status.AutoEllipsis = true;
        cloud.Controls.Add(status, 1, 1);
        cloud.Controls.Add(check, 2, 1);
        check.Click += async (_, _) => await CheckHealthAsync();
        cloudGroup.Controls.Add(cloud);

        cancel.DialogResult = DialogResult.Cancel;
        save.Width = 100;
        cancel.Width = 100;
        save.Click += async (_, _) => await SaveAsync();
        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(save);

        root.Controls.Add(cloudGroup, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        baseUrl.Text = initialBaseUrl;
    }

    private void UpdateState(string? message = null, bool error = false, string details = "")
    {
        stateError = error;
        var enrolled = HasCloudIdentity(settings) && SameEndpoint(settings.CloudBaseUrl, baseUrl.Text);
        status.Text = message ?? (baseUrl.Text.Trim().Length == 0
            ? "請由 CYID「直接加入雲端」綁定 Workspace。"
            : enrolled ? "CYID Device 已註冊｜可以測試連線" : "等待 CYID 連線檢查");
        status.ForeColor = error
            ? Color.FromArgb(180, 0, 0)
            : enrolled ? Color.FromArgb(0, 120, 60) : SystemColors.ControlText;
        status.AccessibleDescription = details;
        toolTip.SetToolTip(status, details);
    }

    private async Task CheckHealthAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await CheckConnectionCoreAsync();
            confirmedBaseUrl = result.BaseUrl;
            SelectedOnboardingStatus = result.Onboarding;
            confirmedSummary = ConnectionSummary(result.Onboarding);
            var details = ConnectionDetails(result.Health, result.Onboarding);
            if (HasCloudIdentity(settings) && SameEndpoint(settings.CloudBaseUrl, result.BaseUrl))
                details += "\nCYID Device 已註冊；若要遷移到其他 Workspace，需重新完成 CYID Device 加入。";
            UpdateState(confirmedSummary, details: details);
        });
    }

    private async Task<(string BaseUrl, CloudHealthResult Health, CloudOnboardingStatus Onboarding)> CheckConnectionCoreAsync()
    {
        var normalized = NormalizeBaseUrl(baseUrl.Text);
        var client = new CloudClient(httpClient, new Uri(normalized, UriKind.Absolute));
        var health = await client.CheckHealthAsync(lifetime.Token);
        var problem = CloudCompatibility.CyIdEnrollmentProblem(health);
        if (problem.Length != 0) throw new InvalidOperationException(problem);

        var onboarding = await client.GetOnboardingStatusAsync(lifetime.Token);
        return (normalized, health, onboarding);
    }

    private async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var normalized = NormalizeBaseUrl(baseUrl.Text);
            if (!SameEndpoint(confirmedBaseUrl, normalized) || SelectedOnboardingStatus is null)
            {
                var result = await CheckConnectionCoreAsync();
                normalized = result.BaseUrl;
                confirmedBaseUrl = result.BaseUrl;
                SelectedOnboardingStatus = result.Onboarding;
                confirmedSummary = ConnectionSummary(result.Onboarding);
            }

            SelectedBaseUrl = normalized;
            DialogResult = DialogResult.OK;
            Close();
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        save.Enabled = false;
        check.Enabled = false;
        baseUrl.Enabled = false;
        UpdateState("處理中…");
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (CloudApiException error)
        {
            if (!IsDisposed)
            {
                var details = $"Cloud API 錯誤：{error.Code}\n{error.Message}";
                UpdateState($"Cloud API 回應失敗：{error.Code}", error: true, details: details);
                MessageBox.Show(this, details, "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                UpdateState(error.Message, error: true, details: error.Message);
                MessageBox.Show(this, error.Message, "無法完成雲端連線設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
                save.Enabled = true;
                check.Enabled = true;
                baseUrl.Enabled = true;
                var finalMessage = stateError ? status.Text : confirmedSummary.Length == 0 ? status.Text : confirmedSummary;
                UpdateState(finalMessage, stateError, status.AccessibleDescription ?? string.Empty);
            }
        }
    }

    private static string ConnectionSummary(CloudOnboardingStatus onboarding)
    {
        var workspace = onboarding.WorkspaceInitialized ? "已建立雲端空間" : "尚未建立雲端空間";
        return $"連線正常｜{workspace}";
    }

    private static string ConnectionDetails(CloudHealthResult health, CloudOnboardingStatus onboarding)
    {
        var workspace = onboarding.WorkspaceInitialized
            ? "CYInvoice Workspace 已啟用；新裝置請透過 CYID 邀請或授權配對加入。"
            : "CYID Workspace 尚未啟用，請檢查管理員的 CYID 與 CYInvoice Cloud 設定。";
        return $"{CloudCompatibility.SuccessSummary(health)}\n{workspace}";
    }

    private static string NormalizeBaseUrl(string value)
    {
        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Cloud API 網址必須是有效的 HTTPS 網址，且不可包含帳密、Query 或 Fragment。");

        var normalized = uri.AbsoluteUri;
        if (!normalized.EndsWith("/", StringComparison.Ordinal)) normalized += "/";
        return normalized;
    }

    private static bool HasCloudIdentity(Settings settings) =>
        settings.CloudWorkspaceId.Length != 0
        && settings.CloudDeviceId.Length != 0
        && settings.CloudDeviceTokenEncrypted.Length != 0;

    private static bool SameEndpoint(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return false;
        try
        {
            return string.Equals(NormalizeBaseUrl(left), NormalizeBaseUrl(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "雲端連線設定" || ShowIcon || AcceptButton is not null)
            throw new InvalidOperationException("雲端連線設定視窗基本屬性不正確");
        if (save.Text != "儲存" || cancel.DialogResult != DialogResult.Cancel || check.Text != "測試連線")
            throw new InvalidOperationException("CYID 雲端連線設定動作按鈕不正確");
        if (initialBaseUrl.Length == 0 && baseUrl.Text.Length != 0)
            throw new InvalidOperationException("Public client 不得內建任何 Cloud API endpoint");
        if (baseUrl.PlaceholderText.Contains("workers.dev", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cloud API 輸入不得綁定特定雲端供應商");
        var logicalHeight = ClientSize.Height * 96D / DeviceDpi;
        if (logicalHeight > 205)
            throw new InvalidOperationException("雲端連線設定視窗未維持精簡高度");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            httpClient.Dispose();
            toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}
