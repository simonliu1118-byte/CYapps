using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudDeviceManagementForm : Form
{
    private const int WindowWidth = 760;
    private const int WindowHeight = 590;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CloudClient client;
    private readonly CloudDeviceLifecycleClient lifecycleClient;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage trustedDevicesTab = new("可信任裝置");
    private readonly TabPage addDeviceTab = new("新增裝置");
    private readonly DataGridView deviceGrid = new();
    private readonly Label deviceStatus = UiControls.Label(string.Empty);
    private readonly Button refreshDevices = UiControls.StandardButton("重新整理");
    private readonly Button revokeDevice = UiControls.StandardButton("撤銷所選裝置");
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
    private int activeDeviceCount;
    private bool busy;
    private bool statusChecking;
    private bool deviceLoading;
    private bool resourcesDisposed;

    public CloudDeviceManagementForm(string baseUrl, string deviceToken)
    {
        var endpoint = new Uri(NormalizeBaseUrl(baseUrl), UriKind.Absolute);
        client = new CloudClient(httpClient, endpoint, deviceToken);
        lifecycleClient = new CloudDeviceLifecycleClient(httpClient, endpoint, deviceToken);
        Text = "雲端裝置管理";
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
        deviceStatus.Text = "正在讀取 Workspace 的可信任裝置…";
        statusTimer.Tick += async (_, _) => await RefreshJoinStatusAsync();
        statusTimer.Start();
        Shown += async (_, _) =>
        {
            await LoadDevicesAsync();
            await LoadRecentTicketsAsync();
        };
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
            RowCount = 3,
            Padding = new Padding(12),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "雲端 Workspace 裝置管理",
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        }, 0, 0);

        BuildTrustedDevicesTab();
        BuildAddDeviceTab();
        tabs.TabPages.Add(trustedDevicesTab);
        tabs.TabPages.Add(addDeviceTab);
        root.Controls.Add(tabs, 0, 1);

        close.DialogResult = DialogResult.OK;
        close.Width = 100;
        var footer = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
            Margin = Padding.Empty,
        };
        footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
    }

    private void BuildTrustedDevicesTab()
    {
        trustedDevicesTab.BackColor = SystemColors.Control;
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "撤銷後 Device Token 立即失效；歷史 Device 紀錄會保留，不會刪除。最後一台使用中的裝置不能撤銷。",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
        }, 0, 0);

        ConfigureDeviceGrid();
        root.Controls.Add(deviceGrid, 0, 1);
        deviceStatus.Dock = DockStyle.Fill;
        deviceStatus.TextAlign = ContentAlignment.MiddleLeft;
        deviceStatus.AutoEllipsis = false;
        root.Controls.Add(deviceStatus, 0, 2);

        refreshDevices.Width = 105;
        revokeDevice.Width = 135;
        refreshDevices.Click += async (_, _) => await LoadDevicesAsync();
        revokeDevice.Click += async (_, _) => await RevokeSelectedDeviceAsync();
        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(revokeDevice);
        actions.Controls.Add(refreshDevices);
        root.Controls.Add(actions, 0, 3);
        trustedDevicesTab.Controls.Add(root);
    }

    private void ConfigureDeviceGrid()
    {
        deviceGrid.Dock = DockStyle.Fill;
        deviceGrid.ReadOnly = true;
        deviceGrid.AllowUserToAddRows = false;
        deviceGrid.AllowUserToDeleteRows = false;
        deviceGrid.AllowUserToResizeRows = false;
        deviceGrid.MultiSelect = false;
        deviceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        deviceGrid.RowHeadersVisible = false;
        deviceGrid.AutoGenerateColumns = false;
        deviceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        deviceGrid.BackgroundColor = SystemColors.Window;
        deviceGrid.BorderStyle = BorderStyle.FixedSingle;
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "裝置名稱",
            FillWeight = 160,
        });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "狀態",
            FillWeight = 70,
        });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "版本",
            FillWeight = 75,
        });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "加入時間",
            FillWeight = 105,
        });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "撤銷時間",
            FillWeight = 105,
        });
        deviceGrid.SelectionChanged += (_, _) => UpdateActionState();
    }

    private void BuildAddDeviceTab()
    {
        addDeviceTab.BackColor = SystemColors.Control;
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 232));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

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
        fields.Controls.Add(baseUrl, 1, 0);
        var copyUrl = UiControls.StandardButton("複製網址");
        copyUrl.Click += (_, _) => { if (baseUrl.Text.Length != 0) Clipboard.SetText(baseUrl.Text); };
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
        root.Controls.Add(fields, 0, 0);

        status.Dock = DockStyle.Fill;
        status.AutoEllipsis = false;
        status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(status, 0, 1);

        sendOtp.Width = 118;
        generate.Width = 118;
        copy.Width = 118;
        sendInvitation.Width = 160;
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
        actions.Controls.Add(generate);
        actions.Controls.Add(sendInvitation);
        root.Controls.Add(actions, 0, 2);
        addDeviceTab.Controls.Add(root);
    }

    private async Task LoadDevicesAsync(bool silent = false)
    {
        if (deviceLoading || IsDisposed) return;
        deviceLoading = true;
        UpdateActionState();
        try
        {
            var result = await lifecycleClient.GetDevicesAsync(lifetime.Token);
            activeDeviceCount = result.ActiveDeviceCount;
            PopulateDevices(result.Devices);
            deviceStatus.ForeColor = SystemColors.ControlText;
            deviceStatus.Text = $"目前共有 {result.ActiveDeviceCount} 台使用中的可信任裝置；已撤銷裝置保留於歷史清單。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (CloudApiException error)
        {
            deviceStatus.ForeColor = Color.FromArgb(180, 0, 0);
            deviceStatus.Text = CloudErrorMessage(error);
            if (!silent && !IsDisposed)
                MessageBox.Show(this, deviceStatus.Text, "無法讀取裝置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            deviceStatus.ForeColor = Color.FromArgb(180, 0, 0);
            deviceStatus.Text = error.Message;
            if (!silent && !IsDisposed)
                MessageBox.Show(this, error.Message, "無法讀取裝置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            deviceLoading = false;
            if (!IsDisposed && !Disposing) UpdateActionState();
        }
    }

    private void PopulateDevices(IReadOnlyList<CloudManagedDevice> devices)
    {
        var selectedId = SelectedDevice()?.DeviceId;
        deviceGrid.Rows.Clear();
        DataGridViewRow? rowToSelect = null;
        foreach (var device in devices)
        {
            var name = device.Current ? $"{device.DisplayName}（目前）" : device.DisplayName;
            var statusText = device.Status == "active" ? "使用中" : "已撤銷";
            var rowIndex = deviceGrid.Rows.Add(
                name,
                statusText,
                device.ClientVersion,
                DisplayTime(device.PairedAt ?? device.CreatedAt),
                DisplayTime(device.RevokedAt));
            var row = deviceGrid.Rows[rowIndex];
            row.Tag = device;
            if (selectedId == device.DeviceId || (selectedId is null && device.Current)) rowToSelect = row;
        }
        if (rowToSelect is not null)
        {
            rowToSelect.Selected = true;
            deviceGrid.CurrentCell = rowToSelect.Cells[0];
        }
        UpdateActionState();
    }

    private CloudManagedDevice? SelectedDevice() =>
        deviceGrid.SelectedRows.Count == 1
            ? deviceGrid.SelectedRows[0].Tag as CloudManagedDevice
            : null;

    private async Task RevokeSelectedDeviceAsync()
    {
        var selected = SelectedDevice();
        if (selected is null || selected.Status != "active") return;
        if (activeDeviceCount <= 1)
        {
            MessageBox.Show(this,
                "最後一台使用中的裝置不能撤銷。請先讓另一台可信任裝置加入 Workspace；若不再使用此 Built-in Cloud Workspace，請在中央管理端手動停用 Workspace。",
                "不能撤銷裝置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var warning = selected.Current
            ? "你選的是目前這台電腦。此操作會退出 Built-in Cloud，並在安全確認撤銷完成後永久刪除這台電腦的所有 CYInvoice 本機資料。Cloud Workspace、其他 Device 與中央資料不會被刪除。確定繼續？"
            : $"撤銷「{selected.DisplayName}」後，該裝置的 Device Token 會立即失效。歷史紀錄仍會保留。確定繼續？";
        if (MessageBox.Show(this, warning, "確認撤銷裝置",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var credentials = new CloudDeviceRevokeAuthenticationForm(selected.DisplayName, selected.Current);
        if (credentials.ShowDialog(this) != DialogResult.OK) return;

        if (selected.Current)
        {
            if (MessageBox.Show(this,
                    "最後確認：CYInvoice 會先完整關閉，停止背景同步後才向 Cloud 撤銷目前 Device。Cloud 明確確認 Device 已撤銷後，才會刪除本機 Data / Cache 並回到首次使用。\n\n若 Cloud 結果不明，本機資料與 Device Token 都會保留，不會猜測成功。\n\n確定立即執行？",
                    "最後確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            LocalResetApplication.Schedule(new LocalResetExecutionRequest(
                LocalResetKind.BuiltInCloud,
                credentials.EmployeeNo,
                credentials.Password));
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await lifecycleClient.RevokeAsync(
                selected.DeviceId,
                credentials.EmployeeNo,
                credentials.Password,
                lifetime.Token);
            await LoadDevicesAsync(silent: true);
            deviceStatus.ForeColor = SystemColors.ControlText;
            deviceStatus.Text = result.AlreadyRevoked
                ? "所選裝置先前已完成撤銷；目前狀態已重新確認。"
                : $"已撤銷「{result.Device.DisplayName}」，其 Device Token 已立即失效。";
        });
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
            var joined = false;
            if (ticket is not null)
            {
                var result = await client.GetJoinTicketStatusAsync("device-pairings", ticket.PairingId, lifetime.Token);
                if (result.Status == "joined")
                {
                    pairingCode.Text = "已成功加入";
                    ticket = null;
                    joined = true;
                    UpdateState($"立即配對成功：{result.JoinedDeviceName} 已加入。");
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
                    joined = true;
                    UpdateState($"新裝置邀請成功：{result.JoinedDeviceName} 已加入。");
                }
            }
            if (joined) await LoadDevicesAsync(silent: true);
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
                deviceStatus.ForeColor = Color.FromArgb(180, 0, 0);
                deviceStatus.Text = message;
                MessageBox.Show(this, message, "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                UpdateState(error.Message, error: true);
                deviceStatus.ForeColor = Color.FromArgb(180, 0, 0);
                deviceStatus.Text = error.Message;
                MessageBox.Show(this, error.Message, "裝置管理失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        refreshDevices.Enabled = !busy && !deviceLoading;
        var selected = SelectedDevice();
        revokeDevice.Enabled = !busy && !deviceLoading && activeDeviceCount > 1
            && selected is { Status: "active" };
        close.Enabled = !busy;
        otp.Enabled = !busy;
    }

    private static string CloudErrorMessage(CloudApiException error) => error.Code switch
    {
        "UNAUTHORIZED" => "目前這台電腦的雲端裝置身分無效，請重新加入 Workspace。",
        "SUPER_ADMIN_AUTH_FAILED" => "超級管理員員工編號或密碼錯誤。",
        "LAST_ACTIVE_DEVICE" => "最後一台使用中的裝置不能撤銷，請先加入另一台可信任裝置。",
        "DEVICE_NOT_FOUND" => "找不到要撤銷的裝置，請重新整理清單。",
        "INVALID_DEVICE" => "裝置識別資料格式錯誤，請重新整理清單。",
        "OTP_INVALID" => "Email 驗證碼錯誤或已失效。",
        "OTP_ATTEMPTS_EXHAUSTED" => "Email 驗證碼錯誤次數已達上限，請重新寄送。",
        "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
        "OTP_RATE_LIMITED" => "驗證碼寄送次數過多，請稍後再試。",
        "WORKSPACE_RECOVERY_EMAIL_NOT_CONFIGURED" => "Workspace 尚未設定可用的超級管理員 Recovery Email。",
        "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud 尚未完成 Email 寄送服務設定。",
        "EMAIL_DELIVERY_UNAVAILABLE" => "驗證信目前無法寄出，請稍後再試。",
        _ => $"Cloud API 錯誤：{error.Code}\n{error.Message}"
    };

    private static string DisplayTime(DateTimeOffset? value) =>
        value is null ? string.Empty : value.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

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
        if (Text != "雲端裝置管理" || ShowIcon || AcceptButton is not null || CancelButton != close)
            throw new InvalidOperationException("裝置管理視窗基本屬性不正確");
        if (otp.MaxLength != 6 || !pairingCode.ReadOnly || !baseUrl.ReadOnly
            || generate.Text != "產生配對碼" || tabs.TabPages.Count != 2 || !deviceGrid.ReadOnly)
            throw new InvalidOperationException("裝置管理驗證碼、裝置清單或配對碼欄位設定不正確");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        var logicalHeight = ClientSize.Height * 96D / DeviceDpi;
        if (logicalWidth > 775 || logicalHeight > 607)
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
