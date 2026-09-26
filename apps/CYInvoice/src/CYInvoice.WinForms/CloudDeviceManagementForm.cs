using CYInvoice.Core.Cloud;

namespace CYInvoice.WinForms;

internal sealed class CloudDeviceManagementForm : Form
{
    private const int WindowWidth = 620;
    private const int WindowHeight = 440;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CloudClient client;
    private readonly Label status = UiControls.Label(string.Empty);
    private readonly TextBox otp = UiControls.TextBox(6);
    private readonly TextBox pairingCode = UiControls.TextBox(32);
    private readonly TextBox baseUrl = UiControls.TextBox(200);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly TextBox invitation = UiControls.TextBox(100);
    private readonly Button sendOtp = UiControls.StandardButton("寄送驗證碼");
    private readonly Button generate = UiControls.StandardButton("產生配對碼");
    private readonly Button copy = UiControls.StandardButton("複製配對碼");
    private readonly Button sendInvitation = UiControls.StandardButton("寄送新裝置邀請");
    private readonly Button revokeInvitation = UiControls.StandardButton("撤銷邀請");
    private readonly Button close = UiControls.StandardButton("關閉");
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 5000 };
    private CloudEmailChallenge? challenge;
    private CloudPairingTicket? ticket;
    private CloudInvitationTicket? invitationTicket;
    private bool busy;
    private bool statusChecking;
    private bool resourcesDisposed;

    public CloudDeviceManagementForm(string baseUrl, string deviceToken)
    {
        client = new CloudClient(httpClient, new Uri(NormalizeBaseUrl(baseUrl), UriKind.Absolute), deviceToken);
        Text = "新增雲端裝置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        this.baseUrl.Text = NormalizeBaseUrl(baseUrl).TrimEnd('/');
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        BuildLayout();
        UpdateState("立即配對：超管驗證並收取 Email 驗證碼。邀請：超管驗證後將網址與開通碼寄到已驗證信箱。");
        statusTimer.Tick += async (_, _) => await RefreshJoinStatusAsync();
        statusTimer.Start();
        Shown += async (_, _) => await LoadRecentTicketsAsync();
    }

    private void BuildLayout()
    {
        otp.TextAlign = HorizontalAlignment.Center;
        pairingCode.ReadOnly = true;
        pairingCode.TabStop = false;
        pairingCode.TextAlign = HorizontalAlignment.Center;
        baseUrl.ReadOnly = true;
        invitation.ReadOnly = true;
        password.UseSystemPasswordChar = true;

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "新增可信任裝置：立即配對或寄送邀請",
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };
        root.Controls.Add(header, 0, 0);

        var fields = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        for (var row = 0; row < 6; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        fields.Controls.Add(FieldLabel("Cloud API 網址"), 0, 0);
        fields.Controls.Add(this.baseUrl, 1, 0);
        var copyUrl = UiControls.StandardButton("複製網址");
        copyUrl.Click += (_, _) => { if (this.baseUrl.Text.Length != 0) Clipboard.SetText(this.baseUrl.Text); };
        fields.Controls.Add(copyUrl, 2, 0);
        fields.Controls.Add(FieldLabel("超管員工編號"), 0, 1);
        fields.Controls.Add(employeeNo, 1, 1);
        fields.Controls.Add(FieldLabel("超管密碼"), 0, 2);
        fields.Controls.Add(password, 1, 2);
        fields.Controls.Add(FieldLabel("Email 驗證碼"), 0, 3);
        fields.Controls.Add(otp, 1, 3);
        fields.Controls.Add(sendOtp, 2, 3);
        fields.Controls.Add(FieldLabel("配對碼"), 0, 4);
        fields.Controls.Add(pairingCode, 1, 4);
        fields.Controls.Add(copy, 2, 4);
        fields.Controls.Add(FieldLabel("邀請狀態"), 0, 5);
        fields.Controls.Add(invitation, 1, 5);
        fields.Controls.Add(revokeInvitation, 2, 5);
        root.Controls.Add(fields, 0, 1);

        status.AutoEllipsis = false;
        status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(status, 0, 2);

        close.DialogResult = DialogResult.OK;
        sendOtp.Width = 118;
        generate.Width = 118;
        copy.Width = 118;
        sendInvitation.Width = 160;
        close.Width = 100;
        sendOtp.Click += async (_, _) => await SendOtpAsync();
        generate.Click += async (_, _) => await GeneratePairingCodeAsync();
        copy.Click += (_, _) => CopyPairingCode();
        sendInvitation.Click += async (_, _) => await SendInvitationAsync();
        revokeInvitation.Click += async (_, _) => await RevokeInvitationAsync();
        otp.TextChanged += (_, _) => UpdateActionState();
        employeeNo.TextChanged += (_, _) => UpdateActionState();
        password.TextChanged += (_, _) => UpdateActionState();

        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(close);
        actions.Controls.Add(generate);
        actions.Controls.Add(sendInvitation);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
    }

    private async Task SendOtpAsync()
    {
        await RunBusyAsync(async () =>
        {
            challenge = await client.StartPairingAuthorizationAsync(employeeNo.Text.Trim(), password.Text, lifetime.Token);
            ticket = null;
            pairingCode.Text = string.Empty;
            otp.Clear();
            UpdateState(
                $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}；" +
                $"可於 {challenge.ResendAfter.ToLocalTime():HH:mm:ss} 後重新寄送。");
            otp.Focus();
        });
    }

    private async Task GeneratePairingCodeAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (challenge is null)
                throw new InvalidOperationException("請先寄送超級管理員 Email 驗證碼。");
            if (DateTimeOffset.UtcNow >= challenge.ExpiresAt.ToUniversalTime())
                throw new InvalidOperationException("Email 驗證碼已過期，請重新寄送。");
            var code = otp.Text.Trim();
            if (code.Length != 6 || !code.All(char.IsDigit))
                throw new InvalidOperationException("Email 驗證碼必須是 6 碼數字。");

            ticket = await client.CreatePairingAsync(challenge.ChallengeId, code,
                employeeNo.Text.Trim(), password.Text, lifetime.Token);
            pairingCode.Text = FormatPairingCode(ticket.Code);
            UpdateState(
                $"配對碼已產生，有效至 {ticket.ExpiresAt.ToLocalTime():HH:mm:ss}。" +
                "請只交給要加入此 Workspace 的那台電腦；使用一次後即失效。");
        });
    }

    private void CopyPairingCode()
    {
        if (ticket is null || ticket.Code.Length == 0) return;
        try
        {
            Clipboard.SetText(ticket.Code);
            UpdateState($"配對碼已複製；有效至 {ticket.ExpiresAt.ToLocalTime():HH:mm:ss}。");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法複製配對碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SendInvitationAsync()
    {
        await RunBusyAsync(async () =>
        {
            invitationTicket = await client.IssueInvitationAsync(employeeNo.Text.Trim(), password.Text, lifetime.Token);
            invitation.Text = $"已寄送，至 {invitationTicket.ExpiresAt.ToLocalTime():MM/dd HH:mm}";
            UpdateState("Cloud API 網址和一次性開通碼已寄至超管已驗證 Email。可在有效期內撤銷；B 機加入後會顯示結果。");
        });
    }

    private async Task RevokeInvitationAsync()
    {
        if (invitationTicket is null) return;
        await RunBusyAsync(async () =>
        {
            await client.RevokeInvitationAsync(invitationTicket.InvitationId,
                employeeNo.Text.Trim(), password.Text, lifetime.Token);
            invitation.Text = "已撤銷";
            invitationTicket = null;
            UpdateState("邀請已撤銷，原開通碼不能再使用。");
        });
    }

    private async Task RefreshJoinStatusAsync()
    {
        if (busy || statusChecking || IsDisposed || (ticket is null && invitationTicket is null)) return;
        statusChecking = true;
        try
        {
            if (ticket is not null)
            {
                var result = await client.GetJoinTicketStatusAsync("device-pairings", ticket.PairingId, lifetime.Token);
                if (result.Status == "joined")
                {
                    pairingCode.Text = "已成功加入";
                    ticket = null;
                    UpdateState($"立即配對成功：{result.JoinedDeviceName} 已加入。 ");
                }
            }
            if (invitationTicket is not null)
            {
                var result = await client.GetJoinTicketStatusAsync("device-invitations",
                    invitationTicket.InvitationId, lifetime.Token);
                if (result.Status == "joined")
                {
                    invitation.Text = $"已成功加入：{result.JoinedDeviceName}";
                    invitationTicket = null;
                    UpdateState($"新裝置邀請成功：{result.JoinedDeviceName} 已加入。");
                }
            }
        }
        catch (Exception error) when (error is CloudApiException or HttpRequestException or TaskCanceledException)
        {
            if (!IsDisposed) UpdateState("暫時無法更新新機加入狀態，稍後會自動重試。", error: true);
        }
        finally { statusChecking = false; }
    }

    private async Task LoadRecentTicketsAsync()
    {
        try
        {
            var recent = await client.GetRecentJoinTicketsAsync(lifetime.Token);
            if (recent.Pairing is { } pairing)
            {
                if (pairing.Status == "pending")
                    ticket = new CloudPairingTicket(pairing.Id, string.Empty, pairing.ExpiresAt);
                else if (pairing.Status == "joined")
                    UpdateState($"上次立即配對已成功：{pairing.JoinedDeviceName} 已加入。");
            }
            if (recent.Invitation is { } issued)
            {
                if (issued.Status == "pending")
                {
                    invitationTicket = new CloudInvitationTicket(issued.Id, issued.ExpiresAt);
                    invitation.Text = $"已寄送，至 {issued.ExpiresAt.ToLocalTime():MM/dd HH:mm}";
                }
                else if (issued.Status == "joined")
                    invitation.Text = $"已成功加入：{issued.JoinedDeviceName}";
                else invitation.Text = issued.Status == "revoked" ? "已撤銷" : "已失效";
            }
            UpdateActionState();
        }
        catch (Exception error) when (error is CloudApiException or HttpRequestException or TaskCanceledException)
        {
            if (!IsDisposed) UpdateState("暫時無法讀取先前的新機加入狀態。", error: true);
        }
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
                MessageBox.Show(this, error.Message, "無法新增裝置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        var validOtp = otp.Text.Length == 6 && otp.Text.All(char.IsDigit);
        var validOwner = employeeNo.TextLength == 4 && employeeNo.Text.All(char.IsDigit)
            && password.TextLength > 0;
        sendOtp.Enabled = !busy && validOwner;
        generate.Enabled = !busy && validOwner && challenge is not null && validOtp;
        copy.Enabled = !busy && ticket is not null && ticket.Code.Length > 0;
        sendInvitation.Enabled = !busy && validOwner;
        revokeInvitation.Enabled = !busy && invitationTicket is not null;
        close.Enabled = !busy;
        otp.Enabled = !busy;
    }

    private static string CloudErrorMessage(CloudApiException error) => error.Code switch
    {
        "UNAUTHORIZED" => "目前這台電腦的雲端裝置身分無效，請先重新加入 Workspace。",
        "OTP_INVALID" => "Email 驗證碼錯誤或已失效。",
        "OTP_ATTEMPTS_EXHAUSTED" => "Email 驗證碼錯誤次數已達上限，請重新寄送。",
        "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
        "OTP_RATE_LIMITED" => "驗證碼寄送次數過多，請稍後再試。",
        "WORKSPACE_RECOVERY_EMAIL_NOT_CONFIGURED" => "Workspace 尚未設定可用的超級管理員 Recovery Email。",
        "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud 尚未完成 Email 寄送服務設定。",
        "EMAIL_DELIVERY_UNAVAILABLE" => "驗證信目前無法寄出，請稍後再試。",
        _ => $"Cloud API 錯誤：{error.Code}\n{error.Message}"
    };

    private static string FormatPairingCode(string code)
    {
        var normalized = new string(code.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (normalized.Length != 20) return code;
        return string.Join("-", Enumerable.Range(0, 5).Select(index => normalized.Substring(index * 4, 4)));
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
        if (Text != "新增雲端裝置" || ShowIcon || AcceptButton is not null || CancelButton != close)
            throw new InvalidOperationException("裝置管理視窗基本屬性不正確");
        if (otp.MaxLength != 6 || !pairingCode.ReadOnly || !baseUrl.ReadOnly
            || generate.Text != "產生配對碼")
            throw new InvalidOperationException("裝置管理驗證碼或配對碼欄位設定不正確");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        var logicalHeight = ClientSize.Height * 96D / DeviceDpi;
        if (logicalWidth > 635 || logicalHeight > 457)
            throw new InvalidOperationException("裝置管理視窗尺寸異常");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            statusTimer.Dispose();
            httpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
