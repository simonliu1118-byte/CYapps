using System.Net;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudJoinWorkspaceForm : Form
{
    private const int WindowWidth = 520;
    private const int WindowHeight = 278;
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly string baseUrl;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox pairingCode = UiControls.TextBox(32);
    private readonly TextBox deviceName = UiControls.TextBox(120);
    private readonly Label status = UiControls.Label(string.Empty);
    private readonly Button join = UiControls.StandardButton("加入雲端空間");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private bool busy;
    private bool resourcesDisposed;

    public CloudJoinWorkspaceForm(LocalRepository repository, Settings settings, string baseUrl)
    {
        this.repository = repository;
        this.settings = settings;
        this.baseUrl = NormalizeBaseUrl(baseUrl);

        Text = "加入既有雲端空間";
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
        UpdateState("請輸入由已加入裝置完成超級管理員 Email 驗證後產生的配對碼。");
        Shown += async (_, _) => await RecoverPendingOnShownAsync();
    }

    public bool IdentityCompleted { get; private set; }

    private void BuildLayout()
    {
        pairingCode.CharacterCasing = CharacterCasing.Lower;
        pairingCode.TextAlign = HorizontalAlignment.Center;

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "這台電腦尚未加入此 Workspace。配對碼只授權裝置加入，不會把本機帳號自動升級成雲端超級管理員。",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = Padding.Empty,
        };
        root.Controls.Add(header, 0, 0);

        var fields = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        fields.Controls.Add(FieldLabel("配對碼"), 0, 0);
        fields.Controls.Add(pairingCode, 1, 0);
        fields.Controls.Add(FieldLabel("裝置名稱"), 0, 1);
        fields.Controls.Add(deviceName, 1, 1);
        root.Controls.Add(fields, 0, 1);

        status.AutoEllipsis = false;
        status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(status, 0, 2);

        cancel.DialogResult = DialogResult.Cancel;
        join.Width = 132;
        cancel.Width = 100;
        join.Click += async (_, _) => await JoinAsync();
        pairingCode.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, deviceName);
        deviceName.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            join.PerformClick();
        };

        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(join);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        deviceName.Text = Environment.MachineName.Length <= 120
            ? Environment.MachineName
            : Environment.MachineName[..120];

        var pendingJoin = repository.Settings.CloudPendingDeviceJoin(settings);
        if (pendingJoin is not null && SameEndpoint(pendingJoin.BaseUrl, baseUrl))
        {
            deviceName.Text = pendingJoin.DeviceDisplayName;
            status.Text = "已找到未完成的裝置加入資料，會先用原本已保護的 Device Token 嘗試找回。";
            return;
        }

        var pendingBootstrap = repository.Settings.CloudPendingBootstrap(settings);
        if (pendingBootstrap is not null && SameEndpoint(pendingBootstrap.BaseUrl, baseUrl))
            status.Text = "已找到先前第一台裝置的 Pending Token，會先確認是否其實已完成建立。";
    }

    private async Task RecoverPendingOnShownAsync()
    {
        var pendingBootstrap = repository.Settings.CloudPendingBootstrap(settings);
        var pendingJoin = repository.Settings.CloudPendingDeviceJoin(settings);
        if ((pendingBootstrap is null || !SameEndpoint(pendingBootstrap.BaseUrl, baseUrl))
            && (pendingJoin is null || !SameEndpoint(pendingJoin.BaseUrl, baseUrl)))
            return;

        await RunBusyAsync(async () =>
        {
            if (pendingBootstrap is not null && SameEndpoint(pendingBootstrap.BaseUrl, baseUrl))
            {
                var result = await ProbeTokenAsync(pendingBootstrap.DeviceToken);
                if (result == RecoveryResult.Recovered) return;
                if (result == RecoveryResult.Unavailable)
                    throw new InvalidOperationException("目前無法確認先前第一台裝置是否已在 Cloud 建立。Pending Token 已保留，請稍後再試，不會建立第二個裝置。");

                repository.Settings.ClearCloudPendingBootstrap(settings);
                repository.Settings.Save(settings);
                UpdateState("先前第一台裝置 Token 未在此 Workspace 註冊；可以改用正式配對碼加入這台電腦。");
            }

            pendingJoin = repository.Settings.CloudPendingDeviceJoin(settings);
            if (pendingJoin is null || !SameEndpoint(pendingJoin.BaseUrl, baseUrl)) return;
            var joinResult = await ProbeTokenAsync(pendingJoin.DeviceToken);
            if (joinResult == RecoveryResult.Recovered) return;
            if (joinResult == RecoveryResult.Unavailable)
                throw new InvalidOperationException("目前無法確認先前的裝置加入結果。Pending Token 已保留，請稍後再試。");
            UpdateState("先前的 Device Join 尚未在 Cloud 完成。請輸入有效配對碼；本次會沿用原本的 Pending Device Token。");
        });
    }

    private async Task JoinAsync()
    {
        await RunBusyAsync(async () =>
        {
            var anonymous = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute));
            var onboarding = await anonymous.GetOnboardingStatusAsync(lifetime.Token);
            if (!onboarding.WorkspaceInitialized)
                throw new InvalidOperationException("此 Cloud 尚未建立 Workspace，不能使用裝置加入流程。");

            var pendingBootstrap = repository.Settings.CloudPendingBootstrap(settings);
            if (pendingBootstrap is not null)
            {
                if (!SameEndpoint(pendingBootstrap.BaseUrl, baseUrl))
                    throw new InvalidOperationException("另一個 Cloud 尚有未完成的第一台裝置初始化資料；為避免遺失復原憑證，請先回原 Cloud 處理。");
                var bootstrapRecovery = await ProbeTokenAsync(pendingBootstrap.DeviceToken);
                if (bootstrapRecovery == RecoveryResult.Recovered) return;
                if (bootstrapRecovery == RecoveryResult.Unavailable)
                    throw new InvalidOperationException("目前無法確認先前第一台裝置結果。Pending Token 已保留，暫不送出新的裝置加入要求。");
                repository.Settings.ClearCloudPendingBootstrap(settings);
                repository.Settings.Save(settings);
            }

            var code = NormalizePairingCode(pairingCode.Text);
            if (code.Length != 20)
                throw new InvalidOperationException("配對碼必須是 20 碼十六進位字元。");
            var displayName = deviceName.Text.Trim();
            if (displayName.Length is < 1 or > 120)
                throw new InvalidOperationException("裝置名稱長度必須為 1 到 120 個字元。");

            var pendingJoin = repository.Settings.CloudPendingDeviceJoin(settings);
            if (pendingJoin is not null && !SameEndpoint(pendingJoin.BaseUrl, baseUrl))
                throw new InvalidOperationException("另一個 Cloud 尚有未完成的裝置加入資料；為避免遺失復原憑證，請先回原 Cloud 處理。");

            var attempt = pendingJoin is null
                ? CloudDeviceJoinAttempt.Create()
                : new CloudDeviceJoinAttempt(pendingJoin.DeviceToken);
            var startedAt = pendingJoin?.StartedAtUtc ?? DateTimeOffset.UtcNow;

            repository.Settings.SetCloudPendingDeviceJoin(
                settings,
                baseUrl,
                displayName,
                attempt.DeviceToken,
                startedAt);
            settings.CloudBaseUrl = baseUrl;
            repository.Settings.Save(settings);

            CloudDeviceIdentity claimed;
            try
            {
                claimed = await anonymous.ClaimPairingAsync(
                    code,
                    displayName,
                    Application.ProductVersion,
                    attempt,
                    lifetime.Token);
            }
            catch (Exception error) when (CouldBeAmbiguousClaim(error))
            {
                var recovered = await ProbeTokenAsync(attempt.DeviceToken);
                if (recovered == RecoveryResult.Recovered) return;
                if (recovered == RecoveryResult.Unavailable)
                    throw new InvalidOperationException("裝置加入結果目前無法確認。Pending Device Token 已安全保留；請稍後重新開啟此流程確認，不要連續重建裝置。", error);
                throw;
            }

            var authenticated = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute), attempt.DeviceToken);
            var verified = await authenticated.GetCurrentDeviceAsync(lifetime.Token);
            if (!string.Equals(claimed.WorkspaceId, verified.WorkspaceId, StringComparison.Ordinal)
                || !string.Equals(claimed.DeviceId, verified.DeviceId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Cloud 配對結果與 Device 驗證結果不一致。Pending identity 已保留，未寫入正式身分。");
            }

            CommitIdentity(verified, attempt.DeviceToken);
            CompleteSuccess("這台電腦已加入既有雲端空間，Device identity 驗證完成。");
        });
    }

    private async Task<RecoveryResult> ProbeTokenAsync(string token)
    {
        try
        {
            var client = new CloudClient(httpClient, new Uri(baseUrl, UriKind.Absolute), token);
            var identity = await client.GetCurrentDeviceAsync(lifetime.Token);
            CommitIdentity(identity, token);
            CompleteSuccess("已找回先前完成的雲端裝置，不會建立第二個 Device。");
            return RecoveryResult.Recovered;
        }
        catch (CloudApiException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            return RecoveryResult.NotRegistered;
        }
        catch (HttpRequestException)
        {
            return RecoveryResult.Unavailable;
        }
        catch (TaskCanceledException) when (!lifetime.IsCancellationRequested)
        {
            return RecoveryResult.Unavailable;
        }
    }

    private void CommitIdentity(CloudDeviceIdentity identity, string token)
    {
        settings.CloudBaseUrl = baseUrl;
        settings.CloudWorkspaceId = identity.WorkspaceId;
        settings.CloudDeviceId = identity.DeviceId;
        repository.Settings.SetCloudDeviceToken(settings, token);
        repository.Settings.ClearCloudPendingBootstrap(settings);
        repository.Settings.ClearCloudPendingDeviceJoin(settings);
        settings.CloudMode = CloudModes.CloudPreferred;
        repository.Settings.Save(settings);
        IdentityCompleted = true;
    }

    private void CompleteSuccess(string message)
    {
        UpdateState(message);
        MessageBox.Show(this, message, "裝置加入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
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
                MessageBox.Show(this, error.Message, "無法加入雲端空間", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private void UpdateState(string message, bool error = false)
    {
        status.Text = message;
        status.ForeColor = error ? Color.FromArgb(180, 0, 0) : SystemColors.ControlText;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        join.Enabled = !busy;
        cancel.Enabled = !busy;
        pairingCode.Enabled = !busy;
        deviceName.Enabled = !busy;
    }

    private static bool CouldBeAmbiguousClaim(Exception error) =>
        error is HttpRequestException
        or TaskCanceledException
        or CloudApiException { Code: "PAIRING_CODE_USED" };

    private static string CloudErrorMessage(CloudApiException error) => error.Code switch
    {
        "PAIRING_CODE_INVALID" => "配對碼錯誤或已過期，請在已加入的電腦重新產生。",
        "PAIRING_CODE_USED" => "此配對碼已使用。若剛才曾斷線，Pending Token 已保留；重新開啟此流程會先嘗試找回原 Device。",
        "UNAUTHORIZED" => "目前的 Device Token 尚未在此 Workspace 註冊。",
        _ => $"Cloud API 錯誤：{error.Code}\n{error.Message}"
    };

    private static string NormalizePairingCode(string value)
    {
        return new string(value.Where(IsHexCharacter).ToArray()).ToLowerInvariant();
    }

    private static bool IsHexCharacter(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static void AdvanceOnEnter(KeyEventArgs eventArgs, Control next)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        next.Focus();
        if (next is TextBox textBox) textBox.SelectAll();
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
        if (Text != "加入既有雲端空間" || ShowIcon || AcceptButton is not null || CancelButton != cancel)
            throw new InvalidOperationException("裝置加入視窗基本屬性不正確");
        if (pairingCode.MaxLength != 32 || deviceName.MaxLength != 120 || join.Text != "加入雲端空間")
            throw new InvalidOperationException("裝置加入欄位或按鈕設定不正確");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        var logicalHeight = ClientSize.Height * 96D / DeviceDpi;
        if (logicalWidth > 535 || logicalHeight > 295)
            throw new InvalidOperationException("裝置加入視窗尺寸異常");
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

    private enum RecoveryResult
    {
        Recovered,
        NotRegistered,
        Unavailable
    }
}
