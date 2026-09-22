using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudFirstWorkspaceForm : Form
{
    private const int WindowWidth = 560;
    private const int WindowHeight = 382;
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly string baseUrl;
    private readonly EmployeeAccount superAdmin;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();

    private readonly Label accountSummary = UiControls.Label(string.Empty);
    private readonly Label status = UiControls.Label(string.Empty);
    private readonly TextBox superAdminPassword = UiControls.TextBox(200);
    private readonly TextBox bootstrapKey = UiControls.TextBox(200);
    private readonly TextBox workspaceName = UiControls.TextBox(120);
    private readonly TextBox deviceName = UiControls.TextBox(120);
    private readonly TextBox emailOtp = UiControls.TextBox(6);
    private readonly Button sendOtp = UiControls.StandardButton("寄送驗證碼");
    private readonly Button createWorkspace = UiControls.StandardButton("建立雲端空間");
    private readonly Button cancel = UiControls.StandardButton("取消");

    private CloudEmailChallenge? challenge;
    private bool busy;
    private bool resourcesDisposed;

    public CloudFirstWorkspaceForm(LocalRepository repository, Settings settings, string baseUrl)
    {
        this.repository = repository;
        this.settings = settings;
        this.baseUrl = NormalizeBaseUrl(baseUrl);
        superAdmin = LoadSingleSuperAdmin(repository.Employees);

        Text = "建立第一個雲端空間";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        BuildLayout();
        LoadValues();
        UpdateState();
        Shown += (_, _) => superAdminPassword.Focus();
    }

    public bool IdentityCompleted { get; private set; }

    private void BuildLayout()
    {
        superAdminPassword.UseSystemPasswordChar = true;
        bootstrapKey.UseSystemPasswordChar = true;
        emailOtp.TextAlign = HorizontalAlignment.Center;

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 194));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "以本機既有超級管理員建立第一個 Workspace。Email 沿用現有帳號，不會要求重新輸入。",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = Padding.Empty,
        };
        root.Controls.Add(header, 0, 0);

        var fields = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 6; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        fields.Controls.Add(FieldLabel("本機超級管理員"), 0, 0);
        fields.Controls.Add(accountSummary, 1, 0);
        fields.Controls.Add(FieldLabel("本機超管密碼"), 0, 1);
        fields.Controls.Add(superAdminPassword, 1, 1);
        fields.Controls.Add(FieldLabel("雲端初始化碼"), 0, 2);
        fields.Controls.Add(bootstrapKey, 1, 2);
        fields.Controls.Add(FieldLabel("Workspace 名稱"), 0, 3);
        fields.Controls.Add(workspaceName, 1, 3);
        fields.Controls.Add(FieldLabel("裝置名稱"), 0, 4);
        fields.Controls.Add(deviceName, 1, 4);
        fields.Controls.Add(FieldLabel("Email 驗證碼"), 0, 5);
        fields.Controls.Add(emailOtp, 1, 5);
        root.Controls.Add(fields, 0, 1);

        status.AutoEllipsis = false;
        status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(status, 0, 2);

        cancel.DialogResult = DialogResult.Cancel;
        sendOtp.Width = 128;
        createWorkspace.Width = 142;
        cancel.Width = 100;
        sendOtp.Click += async (_, _) => await SendOtpAsync();
        createWorkspace.Click += async (_, _) => await CreateWorkspaceAsync();
        emailOtp.TextChanged += (_, _) => UpdateActionState();

        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(createWorkspace);
        actions.Controls.Add(sendOtp);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        accountSummary.Text = $"{superAdmin.EmployeeNo}  {superAdmin.Name}  {MaskEmail(superAdmin.Email)}";
        deviceName.Text = Environment.MachineName.Length <= 120
            ? Environment.MachineName
            : Environment.MachineName[..120];

        var pending = repository.Settings.CloudPendingBootstrap(settings);
        if (pending is null || !SameEndpoint(pending.BaseUrl, baseUrl)) return;

        workspaceName.Text = pending.WorkspaceDisplayName;
        deviceName.Text = pending.DeviceDisplayName;
        status.Text = "已找到未完成的雲端初始化，後續會沿用原本已保護的 Device Token。";
    }

    private void UpdateState(string? message = null, bool error = false)
    {
        if (message is not null) status.Text = message;
        if (status.Text.Length == 0)
            status.Text = "請先輸入本機超管密碼與雲端初始化碼，再寄送 Email 驗證碼。";
        status.ForeColor = error ? Color.FromArgb(180, 0, 0) : SystemColors.ControlText;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var hasOtp = emailOtp.Text.Length == 6 && emailOtp.Text.All(char.IsDigit);
        sendOtp.Enabled = !busy;
        createWorkspace.Enabled = !busy && challenge is not null && hasOtp;
        cancel.Enabled = !busy;
        superAdminPassword.Enabled = !busy;
        bootstrapKey.Enabled = !busy;
        workspaceName.Enabled = !busy;
        deviceName.Enabled = !busy;
        emailOtp.Enabled = !busy;
    }

    private async Task SendOtpAsync()
    {
        await RunBusyAsync(async () =>
        {
            RequireSuperAdminAuthentication();
            if (string.IsNullOrWhiteSpace(bootstrapKey.Text))
                throw new InvalidOperationException("請輸入雲端初始化碼。");

            var anonymousClient = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute));
            var onboarding = await anonymousClient.GetOnboardingStatusAsync(lifetime.Token);
            if (onboarding.WorkspaceInitialized)
            {
                if (await TryRecoverPendingIdentityAsync()) return;
                throw new InvalidOperationException("此 Cloud 已有 Workspace，這台電腦尚未加入；不可再次建立第一個 Workspace。請改用裝置加入／復原流程。");
            }

            challenge = await anonymousClient.StartBootstrapEmailChallengeAsync(
                bootstrapKey.Text,
                superAdmin.Email,
                lifetime.Token);
            accountSummary.Text = $"{superAdmin.EmployeeNo}  {superAdmin.Name}  {challenge.MaskedEmail}";
            UpdateState(
                $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}；" +
                $"可於 {challenge.ResendAfter.ToLocalTime():HH:mm:ss} 後重新寄送。");
            emailOtp.Focus();
        });
    }

    private async Task CreateWorkspaceAsync()
    {
        await RunBusyAsync(async () =>
        {
            RequireSuperAdminAuthentication();
            if (challenge is null)
                throw new InvalidOperationException("請先寄送 Email 驗證碼。");
            if (DateTimeOffset.UtcNow >= challenge.ExpiresAt.ToUniversalTime())
                throw new InvalidOperationException("Email 驗證碼已過期，請重新寄送。");
            if (string.IsNullOrWhiteSpace(bootstrapKey.Text))
                throw new InvalidOperationException("請輸入雲端初始化碼。");

            var workspace = workspaceName.Text.Trim();
            var device = deviceName.Text.Trim();
            var otp = emailOtp.Text.Trim();
            if (workspace.Length is < 1 or > 120)
                throw new InvalidOperationException("Workspace 名稱長度必須為 1 到 120 個字元。");
            if (device.Length is < 1 or > 120)
                throw new InvalidOperationException("裝置名稱長度必須為 1 到 120 個字元。");
            if (otp.Length != 6 || !otp.All(char.IsDigit))
                throw new InvalidOperationException("Email 驗證碼必須是 6 碼數字。");

            var pending = repository.Settings.CloudPendingBootstrap(settings);
            if (pending is not null && !SameEndpoint(pending.BaseUrl, baseUrl))
                throw new InvalidOperationException("目前另有不同 Cloud API 的未完成初始化資料，為避免遺失復原憑證，請先回到原 Cloud 完成或清除該流程。");

            var attempt = pending is null
                ? CloudBootstrapAttempt.Create()
                : new CloudBootstrapAttempt(pending.DeviceToken);
            var startedAt = pending?.StartedAtUtc ?? DateTimeOffset.UtcNow;

            repository.Settings.SetCloudPendingBootstrap(
                settings,
                baseUrl,
                workspace,
                device,
                attempt.DeviceToken,
                startedAt);
            settings.CloudBaseUrl = baseUrl;
            repository.Settings.Save(settings);

            var client = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute));
            CloudDeviceIdentity created;
            try
            {
                created = await client.BootstrapAsync(
                    bootstrapKey.Text,
                    workspace,
                    device,
                    Application.ProductVersion,
                    challenge.ChallengeId,
                    otp,
                    attempt,
                    lifetime.Token);
            }
            catch (Exception error) when (CouldBeAmbiguousBootstrap(error))
            {
                if (await TryRecoverPendingIdentityAsync()) return;
                throw;
            }

            var authenticatedClient = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute), attempt.DeviceToken);
            var verified = await authenticatedClient.GetCurrentDeviceAsync(lifetime.Token);
            if (!string.Equals(created.WorkspaceId, verified.WorkspaceId, StringComparison.Ordinal)
                || !string.Equals(created.DeviceId, verified.DeviceId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Cloud 建立結果與 Device 驗證結果不一致，Pending identity 已保留，未寫入正式身分。");
            }

            CommitIdentity(verified, attempt.DeviceToken);
            CompleteSuccess("雲端空間與第一台裝置已建立，Device identity 驗證完成；帳號轉換尚未完成。");
        });
    }

    private async Task<bool> TryRecoverPendingIdentityAsync()
    {
        var pending = repository.Settings.CloudPendingBootstrap(settings);
        if (pending is null || !SameEndpoint(pending.BaseUrl, baseUrl)) return false;

        try
        {
            var client = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute), pending.DeviceToken);
            var identity = await client.GetCurrentDeviceAsync(lifetime.Token);
            CommitIdentity(identity, pending.DeviceToken);
            CompleteSuccess("已找回先前完成的雲端裝置，未建立第二個 Workspace／Device；帳號主資料仍維持在轉換狀態。");
            return true;
        }
        catch (CloudApiException error) when (error.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!lifetime.IsCancellationRequested)
        {
            return false;
        }
    }

    private void CommitIdentity(CloudDeviceIdentity identity, string token)
    {
        settings.CloudBaseUrl = baseUrl;
        settings.CloudWorkspaceId = identity.WorkspaceId;
        settings.CloudDeviceId = identity.DeviceId;
        repository.Settings.SetCloudDeviceToken(settings, token);
        repository.Settings.ClearCloudPendingBootstrap(settings);
        repository.Settings.MarkCloudEmployeeTransition(settings);
        repository.Settings.Save(settings);
        IdentityCompleted = true;
    }

    private void CompleteSuccess(string message)
    {
        UpdateState(message);
        MessageBox.Show(this, message, "雲端初始化完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RequireSuperAdminAuthentication()
    {
        var authenticated = repository.Employees.Authenticate(superAdmin.EmployeeNo, superAdminPassword.Text);
        if (authenticated is null || authenticated.Role != EmployeeRoles.SuperAdmin || !authenticated.Enabled)
            throw new InvalidOperationException("本機超級管理員密碼錯誤。");
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        UpdateActionState();
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
                var message = CloudErrorMessage(error);
                UpdateState(message, error: true);
                MessageBox.Show(this, message, "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                UpdateState(error.Message, error: true);
                MessageBox.Show(this, error.Message, "無法完成雲端初始化", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
                UpdateActionState();
            }
        }
    }

    private static bool CouldBeAmbiguousBootstrap(Exception error) =>
        error is HttpRequestException
        or TaskCanceledException
        or CloudApiException { Code: "WORKSPACE_ALREADY_INITIALIZED" };

    private static string CloudErrorMessage(CloudApiException error) => error.Code switch
    {
        "OTP_INVALID" => "Email 驗證碼錯誤。",
        "OTP_EXPIRED" => "Email 驗證碼已過期，請重新寄送。",
        "OTP_ATTEMPTS_EXCEEDED" => "Email 驗證碼錯誤次數已達上限，請重新寄送。",
        "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
        "OTP_RATE_LIMITED" => "驗證碼寄送次數過多，請稍後再試。",
        "BOOTSTRAP_AUTH_FAILED" => "雲端初始化碼錯誤。",
        "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud 尚未完成 Email 寄送服務設定。",
        _ => $"Cloud API 錯誤：{error.Code}\n{error.Message}"
    };

    private static EmployeeAccount LoadSingleSuperAdmin(EmployeeStore employees)
    {
        var matches = employees.LoadAll()
            .Where(account => account.Role == EmployeeRoles.SuperAdmin && account.Enabled)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("建立第一個 Workspace 前，本機必須且只能有一個啟用中的超級管理員。");
        if (string.IsNullOrWhiteSpace(matches[0].Email))
            throw new InvalidOperationException("本機超級管理員尚未設定 Email，請先完成帳號資料後再建立雲端空間。");
        return matches[0];
    }

    private static string MaskEmail(string email)
    {
        var normalized = email.Trim();
        var at = normalized.LastIndexOf('@');
        if (at <= 0 || at == normalized.Length - 1) return "已登記 Email";
        var local = normalized[..at];
        var domain = normalized[(at + 1)..];
        var prefix = local.Length == 0 ? "*" : local[..1];
        return $"{prefix}***@{domain}";
    }

    private static string NormalizeBaseUrl(string value)
    {
        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Cloud API 網址必須是有效的 HTTPS 網址。");
        var normalized = uri.AbsoluteUri;
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private static bool SameEndpoint(string left, string right)
    {
        try
        {
            return string.Equals(NormalizeBaseUrl(left), NormalizeBaseUrl(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
        if (Text != "建立第一個雲端空間" || ShowIcon || AcceptButton is not null || CancelButton != cancel)
            throw new InvalidOperationException("首次雲端初始化視窗基本屬性不正確");
        if (!superAdminPassword.UseSystemPasswordChar || !bootstrapKey.UseSystemPasswordChar || emailOtp.MaxLength != 6)
            throw new InvalidOperationException("首次雲端初始化敏感欄位設定不正確");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        var logicalHeight = ClientSize.Height * 96D / DeviceDpi;
        if (logicalWidth > 575 || logicalHeight > 400)
            throw new InvalidOperationException("首次雲端初始化視窗尺寸異常");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            httpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
