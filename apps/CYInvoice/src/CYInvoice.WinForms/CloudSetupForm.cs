using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudSetupForm : Form
{
    private readonly LocalRepository repository;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox baseUrl = UiControls.TextBox(240);
    private readonly TextBox workspaceName = UiControls.TextBox(120);
    private readonly TextBox deviceName = UiControls.TextBox(120);
    private readonly TextBox bootstrapKey = UiControls.TextBox(160);
    private readonly TextBox pairingCode = UiControls.TextBox(32);
    private readonly Label status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
    };
    private readonly Label pairingHint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        ForeColor = Color.DimGray,
    };
    private readonly Button check = UiControls.StandardButton("檢查連線");
    private readonly Button initialize = UiControls.StandardButton("初始化此電腦");
    private readonly Button createPairing = UiControls.StandardButton("產生配對碼");
    private readonly Button join = UiControls.StandardButton("加入工作區");
    private readonly Button close = UiControls.StandardButton("關閉");
    private bool busy;

    public CloudSetupForm(LocalRepository repository)
    {
        this.repository = repository;
        Text = "雲端設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 450);
        MinimumSize = new Size(560, 450);
        MaximumSize = new Size(560, 450);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        bootstrapKey.UseSystemPasswordChar = true;
        baseUrl.PlaceholderText = "https://...workers.dev/";
        workspaceName.Text = "CYInvoice";
        deviceName.Text = Environment.MachineName;
        pairingCode.CharacterCasing = CharacterCasing.Lower;

        BuildLayout();
        LoadValues();
        UpdateState();
        FormClosed += (_, _) => lifetime.Cancel();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var connection = Group("Cloud Worker", 3);
        AddField(connection, 0, "API 網址", baseUrl);
        connection.Controls.Add(check, 2, 1);
        connection.SetColumnSpan(check, 1);
        connection.Controls.Add(status, 1, 2);
        connection.SetColumnSpan(status, 2);
        check.Click += async (_, _) => await CheckHealthAsync();

        var firstDevice = Group("A 機／第一台電腦", 3);
        AddField(firstDevice, 0, "工作區", workspaceName);
        AddField(firstDevice, 1, "電腦名稱", deviceName);
        AddField(firstDevice, 2, "初始化密鑰", bootstrapKey);
        firstDevice.Controls.Add(initialize, 2, 2);
        initialize.Click += async (_, _) => await BootstrapAsync();

        var otherDevice = Group("B 機／其他電腦", 3);
        otherDevice.Controls.Add(createPairing, 1, 0);
        otherDevice.Controls.Add(pairingHint, 2, 0);
        AddField(otherDevice, 1, "配對碼", pairingCode);
        otherDevice.Controls.Add(join, 2, 2);
        createPairing.Click += async (_, _) => await CreatePairingAsync();
        join.Click += async (_, _) => await ClaimPairingAsync();

        close.DialogResult = DialogResult.OK;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
        };
        actions.Controls.Add(close);

        root.Controls.Add(connection, 0, 0);
        root.Controls.Add(firstDevice, 0, 1);
        root.Controls.Add(otherDevice, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        CancelButton = close;
    }

    private static TableLayoutPanel Group(string title, int rows)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = rows,
            Padding = new Padding(8, 4, 8, 4),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        for (var row = 0; row < rows; row++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
        group.Controls.Add(layout);
        group.Tag = layout;
        return layout;
    }

    private static void AddField(TableLayoutPanel layout, int row, string labelText, Control field)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(3),
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(field, 1, row);
        if (layout.GetControlFromPosition(2, row) is null) layout.SetColumnSpan(field, 2);
    }

    private void LoadValues()
    {
        var settings = repository.Settings.LoadOrCreate();
        baseUrl.Text = settings.CloudBaseUrl;
    }

    private void UpdateState(string? message = null, bool error = false)
    {
        var settings = repository.Settings.LoadOrCreate();
        var registered = settings.CloudMode == CloudModes.CloudPreferred
            && settings.CloudWorkspaceId.Length != 0
            && settings.CloudDeviceId.Length != 0
            && settings.CloudDeviceTokenEncrypted.Length != 0;

        status.Text = message ?? (registered
            ? $"已啟用雲端｜裝置 {ShortId(settings.CloudDeviceId)}"
            : "目前為本機模式；雲端設定不會影響既有開票功能。");
        status.ForeColor = error ? Color.FromArgb(180, 0, 0) : registered ? Color.FromArgb(0, 120, 60) : SystemColors.ControlText;
        createPairing.Enabled = !busy && registered;
        initialize.Enabled = !busy && !registered;
        join.Enabled = !busy && !registered;
        check.Enabled = !busy;
    }

    private async Task CheckHealthAsync()
    {
        await RunBusyAsync(async () =>
        {
            var client = NewClient(authenticated: false);
            var health = await client.CheckHealthAsync(lifetime.Token);
            if (!health.Reachable)
                throw new InvalidOperationException("無法連線到 Cloud Worker，請確認網址與網路連線。");
            if (!health.DatabaseAvailable)
                throw new InvalidOperationException($"Cloud Worker 可連線，但 D1 尚未就緒（{health.ErrorCode}）。");
            if (health.SchemaVersion != "2")
                throw new InvalidOperationException($"Cloud schema 版本不符，目前為 {health.SchemaVersion}，需要 2。");

            SaveBaseUrl();
            UpdateState($"Cloud 正常｜API {health.ApiVersion}｜Schema {health.SchemaVersion}");
        });
    }

    private async Task BootstrapAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (bootstrapKey.Text.Trim().Length == 0)
                throw new InvalidOperationException("請輸入 Cloudflare 設定的初始化密鑰。這組密鑰只使用這一次，不會儲存在本機。");
            var workspace = workspaceName.Text.Trim();
            var device = deviceName.Text.Trim();
            if (workspace.Length == 0 || device.Length == 0)
                throw new InvalidOperationException("工作區與電腦名稱不可空白。");

            var client = NewClient(authenticated: false);
            var identity = await client.BootstrapAsync(
                bootstrapKey.Text,
                workspace,
                device,
                ApplicationVersion.Read(),
                lifetime.Token);
            PersistIdentity(identity);
            bootstrapKey.Clear();
            UpdateState("雲端初始化完成；此電腦已成為第一台受信任裝置。");
            MessageBox.Show(this,
                "雲端初始化完成。\n\nDevice Token 已使用 Windows DPAPI 加密保存在本機，不需要另外抄寫。",
                "雲端設定完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private async Task CreatePairingAsync()
    {
        await RunBusyAsync(async () =>
        {
            var client = NewClient(authenticated: true);
            var ticket = await client.CreatePairingAsync(lifetime.Token);
            pairingCode.Text = ticket.Code;
            pairingHint.Text = $"有效至 {ticket.ExpiresAt.ToLocalTime():HH:mm}";
            try { Clipboard.SetText(ticket.Code); } catch (Exception) { }
            UpdateState("已產生一次性配對碼，並嘗試複製到剪貼簿。10 分鐘內在另一台電腦使用。" );
        });
    }

    private async Task ClaimPairingAsync()
    {
        await RunBusyAsync(async () =>
        {
            var code = pairingCode.Text.Trim();
            var device = deviceName.Text.Trim();
            if (code.Length == 0 || device.Length == 0)
                throw new InvalidOperationException("請輸入配對碼與電腦名稱。");

            var client = NewClient(authenticated: false);
            var identity = await client.ClaimPairingAsync(code, device, ApplicationVersion.Read(), lifetime.Token);
            PersistIdentity(identity);
            pairingCode.Clear();
            UpdateState("此電腦已加入既有 CYInvoice 工作區。");
            MessageBox.Show(this,
                "裝置配對完成。\n\nDevice Token 已使用 Windows DPAPI 加密保存在本機。",
                "裝置配對完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private CloudClient NewClient(bool authenticated)
    {
        var url = baseUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("請輸入有效的 Cloud Worker HTTPS 網址。");
        var token = authenticated
            ? repository.Settings.CloudDeviceToken(repository.Settings.LoadOrCreate())
            : string.Empty;
        return new CloudClient(httpClient, uri, token);
    }

    private void SaveBaseUrl()
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.CloudBaseUrl = baseUrl.Text.Trim();
        repository.Settings.Save(settings);
    }

    private void PersistIdentity(CloudDeviceIdentity identity)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.CloudBaseUrl = baseUrl.Text.Trim();
        settings.CloudWorkspaceId = identity.WorkspaceId;
        settings.CloudDeviceId = identity.DeviceId;
        settings.CloudMode = CloudModes.CloudPreferred;
        repository.Settings.SetCloudDeviceToken(settings, identity.DeviceToken);
        repository.Settings.Save(settings);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
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
            UpdateState($"Cloud 回應失敗：{error.Code}", error: true);
            MessageBox.Show(this, CloudErrorText(error), "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            UpdateState(error.Message, error: true);
            MessageBox.Show(this, error.Message, "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            UseWaitCursor = false;
            UpdateState(status.Text, status.ForeColor == Color.FromArgb(180, 0, 0));
        }
    }

    private static string CloudErrorText(CloudApiException error) => error.Code switch
    {
        "BOOTSTRAP_DISABLED" => "Cloudflare 尚未設定 BOOTSTRAP_KEY。",
        "BOOTSTRAP_AUTH_FAILED" => "初始化密鑰不正確。",
        "WORKSPACE_ALREADY_INITIALIZED" => "這個 Cloud 已經完成第一次初始化；其他電腦請使用配對碼加入。",
        "PAIRING_CODE_INVALID" => "配對碼無效或已逾期，請由已註冊電腦重新產生。",
        "PAIRING_CODE_USED" => "這組配對碼已經使用過，請重新產生。",
        "UNAUTHORIZED" => "本機的 Cloud 裝置憑證已失效或被撤銷。",
        _ => $"Cloud API 錯誤：{error.Code}\n{error.Message}"
    };

    private static string ShortId(string value)
    {
        if (value.Length <= 16) return value;
        return value[..8] + "…" + value[^6..];
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "雲端設定" || ShowIcon || baseUrl.UseSystemPasswordChar || !bootstrapKey.UseSystemPasswordChar)
            throw new InvalidOperationException("雲端設定視窗基本屬性不正確");
        if (workspaceName.Text.Length == 0 || deviceName.Text.Length == 0 || close.DialogResult != DialogResult.OK)
            throw new InvalidOperationException("雲端設定預設值或關閉按鈕不正確");
        if (ClientSize.Width != 560 || ClientSize.Height != 450)
            throw new InvalidOperationException("雲端設定視窗尺寸不正確");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            lifetime.Dispose();
            httpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
