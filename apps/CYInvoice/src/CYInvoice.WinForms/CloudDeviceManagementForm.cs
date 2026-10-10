using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudDeviceManagementForm : Form
{
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CloudDeviceLifecycleClient lifecycleClient;
    private readonly string endpoint;
    private readonly string deviceToken;
    private readonly DataGridView deviceGrid = new();
    private readonly Label deviceCount = UiControls.Label("正在讀取使用中的裝置…");
    private readonly Label deviceStatus = UiControls.Label(string.Empty);
    private readonly Button addDevice = UiControls.StandardButton("新增裝置");
    private readonly Button renameDevice = UiControls.StandardButton("更改裝置名稱");
    private readonly Button refreshDevices = UiControls.StandardButton("重新整理");
    private readonly Button revokeDevice = UiControls.DangerButton("撤銷裝置");
    private readonly Button close = UiControls.StandardButton("關閉");
    private int activeDeviceCount;
    private bool busy;
    private bool deviceLoading;
    private bool resourcesDisposed;

    public CloudDeviceManagementForm(string baseUrl, string deviceToken)
    {
        endpoint = baseUrl;
        this.deviceToken = deviceToken;
        lifecycleClient = new CloudDeviceLifecycleClient(httpClient, new Uri(baseUrl), deviceToken);
        Text = "雲端裝置管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        UpdateActionState();
        Shown += async (_, _) => await LoadDevicesAsync();
    }

    private void BuildLayout()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(14), Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        deviceCount.Dock = DockStyle.Fill;
        deviceCount.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        deviceCount.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(deviceCount, 0, 0);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft,
            Text = "撤銷後 Device Token 立即失效；歷史 Device 紀錄會保留，不會刪除。\n最後一台使用中的裝置不能撤銷。",
        }, 0, 1);
        ConfigureDeviceGrid();
        root.Controls.Add(deviceGrid, 0, 2);
        deviceStatus.Dock = DockStyle.Fill;
        deviceStatus.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(deviceStatus, 0, 3);
        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0),
        };
        addDevice.Width = 105;
        renameDevice.Width = 140;
        refreshDevices.Width = 105;
        revokeDevice.Width = 110;
        close.Width = 90;
        close.DialogResult = DialogResult.Cancel;
        addDevice.Click += async (_, _) =>
        {
            using var form = new CloudAddDeviceForm(endpoint, deviceToken);
            form.ShowDialog(this);
            await LoadDevicesAsync(silent: true);
        };
        renameDevice.Click += async (_, _) => await RenameSelectedDeviceAsync();
        revokeDevice.Click += async (_, _) => await RevokeSelectedDeviceAsync();
        refreshDevices.Click += async (_, _) => await LoadDevicesAsync();
        actions.Controls.AddRange([addDevice, renameDevice, refreshDevices, revokeDevice, close]);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
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
            HeaderText = "使用版本",
            ToolTipText = "裝置最近一次成功回報的程式版本；新版在啟動時自動更新。舊版或離線裝置可能尚未回報。",
            FillWeight = 75,
        });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "加入時間",
            FillWeight = 105,
        });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "最後使用時間",
            FillWeight = 105,
        });
        deviceGrid.SelectionChanged += (_, _) => UpdateActionState();
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
            deviceCount.Text = $"目前有 {result.ActiveDeviceCount} 台使用中的裝置";
            deviceStatus.Text = string.Empty;
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
        foreach (var device in devices.Where(device => device.Status == "active"))
        {
            var name = device.Current ? $"{device.DisplayName}（目前）" : device.DisplayName;
            var rowIndex = deviceGrid.Rows.Add(
                name,
                ApplicationVersion.FormatClientVersion(device.ClientVersion),
                DisplayTime(device.PairedAt ?? device.CreatedAt),
                DisplayTime(device.LastSeenAt));
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
                    "最後確認：CYInvoice 會先完整關閉，停止背景同步後才向 Cloud 撤銷目前 Device。Cloud 明確確認 Device 已撤銷後，才會由重啟後的復原程序清除本機 Data / Cache / Logs 及其他執行資料，並回到首次使用。\n\n若 Cloud 結果不明，本機資料與 Device Token 都會保留，不會猜測成功。\n\n確定立即執行？",
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

    private async Task RenameSelectedDeviceAsync()
    {
        var selected = SelectedDevice();
        if (selected is not { Status: "active" }) return;
        using var form = new CloudDeviceRenameForm(selected.DisplayName);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        await RunBusyAsync(async () =>
        {
            var result = await lifecycleClient.RenameAsync(selected.DeviceId, form.DeviceName,
                form.EmployeeNo, form.Password, lifetime.Token);
            await LoadDevicesAsync(silent: true);
            deviceStatus.Text = $"裝置名稱已改為「{result.DisplayName}」。";
        });
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
                deviceStatus.ForeColor = Color.FromArgb(180, 0, 0);
                deviceStatus.Text = message;
                MessageBox.Show(this, message, "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
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

    private void UpdateActionState()
    {
        var available = !busy && !deviceLoading;
        var selected = SelectedDevice();
        addDevice.Enabled = available;
        refreshDevices.Enabled = available;
        renameDevice.Enabled = available && selected is { Status: "active" };
        revokeDevice.Enabled = available && activeDeviceCount > 1 && selected is { Status: "active" };
        close.Enabled = !busy;
    }

    internal static string CloudErrorMessage(CloudApiException error) => error.Code switch
    {
        "UNAUTHORIZED" => "目前這台電腦的雲端裝置身分無效，請重新加入 Workspace。",
        "SUPER_ADMIN_AUTH_FAILED" => "超級管理員員工編號或密碼錯誤。",
        "LAST_ACTIVE_DEVICE" => "最後一台使用中的裝置不能撤銷，請先加入另一台可信任裝置。",
        "DEVICE_NOT_FOUND" => "找不到所選裝置，請重新整理清單。",
        "DEVICE_UNAVAILABLE" => "裝置或超管權限已變更，請重新整理清單再試。",
        "INVALID_DEVICE_NAME" => "裝置名稱須為 1 至 120 個字，不能包含換行或控制字元。",
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

    internal void VerifySmokeLayout()
    {
        if (Text != "雲端裝置管理" || ShowIcon || AcceptButton is not null || CancelButton != close
            || Controls.OfType<TabControl>().Any() || !deviceGrid.ReadOnly
            || !deviceGrid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText)
                .SequenceEqual(new[] { "裝置名稱", "使用版本", "加入時間", "最後使用時間" })
            || revokeDevice is not ThemedDangerButton)
            throw new InvalidOperationException("裝置管理視窗設定不正確");
        var now = DateTimeOffset.UtcNow;
        var current = new CloudManagedDevice("dev_11111111-1111-4111-8111-111111111111", "A 機", "active", "2.6.14", now, now, now, null, true);
        var revoked = current with { DeviceId = "dev_22222222-2222-4222-8222-222222222222", Status = "revoked", Current = false, RevokedAt = now };
        activeDeviceCount = 1;
        PopulateDevices([current, revoked]);
        if (deviceGrid.Rows.Count != 1 || revokeDevice.Enabled || !renameDevice.Enabled)
            throw new InvalidOperationException("裝置管理使用中篩選／最後一台防護不正確");
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
