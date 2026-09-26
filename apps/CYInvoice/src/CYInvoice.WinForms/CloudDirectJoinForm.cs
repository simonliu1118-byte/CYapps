using System.Net;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudDirectJoinForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly HttpClient http = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox url = UiControls.TextBox(200);
    private readonly RadioButton pairingMethod = new() { Text = "使用配對碼", Checked = true, AutoSize = true };
    private readonly RadioButton ownerMethod = new() { Text = "使用邀請碼與超管帳密", AutoSize = true };
    private readonly TextBox pairingCode = UiControls.TextBox(32);
    private readonly TextBox invitationCode = UiControls.TextBox(80);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly TextBox deviceName = UiControls.TextBox(120);
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button authorize = UiControls.StandardButton("確認 Workspace");
    private readonly Button join = UiControls.StandardButton("加入這個 Workspace");
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
    private CloudWorkspacePreview? preview;
    private bool busy;
    private bool resourcesDisposed;

    public CloudDirectJoinForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "首次開啟：直接加入雲端";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 394);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        url.Text = settings.CloudBaseUrl;
        deviceName.Text = Environment.MachineName[..Math.Min(Environment.MachineName.Length, 120)];
        password.UseSystemPasswordChar = true;
        pairingCode.CharacterCasing = CharacterCasing.Lower;
        invitationCode.CharacterCasing = CharacterCasing.Lower;
        BuildLayout();
        pairingMethod.CheckedChanged += (_, _) => ChangeMethod();
        ownerMethod.CheckedChanged += (_, _) => ChangeMethod();
        url.TextChanged += (_, _) => ResetAuthorization();
        pairingCode.TextChanged += (_, _) => ResetAuthorization();
        invitationCode.TextChanged += (_, _) => ResetAuthorization();
        employeeNo.TextChanged += (_, _) => ResetAuthorization();
        ChangeMethod();
        Shown += async (_, _) => await RecoverPendingAsync();
    }

    public bool IdentityCompleted { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
            RowCount = 9, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 7; row++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        AddField(root, 0, "Cloud API 網址", url);
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        modes.Controls.Add(pairingMethod);
        modes.Controls.Add(ownerMethod);
        root.Controls.Add(modes, 1, 1);
        AddField(root, 2, "配對碼", pairingCode);
        AddField(root, 3, "邀請碼", invitationCode);
        AddField(root, 4, "超管員工編號", employeeNo);
        AddField(root, 5, "超管密碼", password);
        AddField(root, 6, "這台裝置名稱", deviceName);
        root.Controls.Add(status, 1, 7);
        var cancel = UiControls.StandardButton("取消");
        cancel.DialogResult = DialogResult.Cancel;
        authorize.Width = 130;
        join.Width = 150;
        cancel.Width = 90;
        authorize.Click += async (_, _) => await AuthorizeAsync();
        join.Click += async (_, _) => await JoinAsync();
        actions.Controls.Add(cancel);
        actions.Controls.Add(join);
        actions.Controls.Add(authorize);
        root.Controls.Add(actions, 1, 8);
        Controls.Add(root);
        CancelButton = cancel;
    }

    internal void VerifySmokeLayout()
    {
        if (ClientSize.Width > 595 || ClientSize.Height > 410)
            throw new InvalidOperationException("雲端加入視窗尺寸異常");
        foreach (Control button in actions.Controls)
        {
            if (button.Left < 0 || button.Top < 0 || button.Right > actions.ClientSize.Width
                || button.Bottom > actions.ClientSize.Height)
                throw new InvalidOperationException("雲端加入視窗操作按鈕被裁切");
        }
    }

    private static void AddField(TableLayoutPanel root, int row, string name, Control field)
    {
        root.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        field.Dock = DockStyle.Fill;
        root.Controls.Add(field, 1, row);
    }

    private void ChangeMethod()
    {
        pairingCode.Enabled = pairingMethod.Checked;
        invitationCode.Enabled = employeeNo.Enabled = password.Enabled = ownerMethod.Checked;
        ResetAuthorization();
    }

    private void ResetAuthorization()
    {
        preview = null;
        join.Enabled = false;
        status.Text = "先確認 Workspace，再加入這台裝置。";
    }

    private CloudClient Client() => new(http, BaseUri());

    private Uri BaseUri()
    {
        var value = url.Text.Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("請輸入有效的 HTTPS Cloud API 網址。");
        return uri;
    }

    private async Task AuthorizeAsync()
    {
        await RunAsync(async () =>
        {
            var client = Client();
            var health = await client.CheckHealthAsync(lifetime.Token);
            var problem = CloudCompatibility.Problem(health);
            if (problem.Length != 0) throw new InvalidOperationException(problem);
            if (pairingMethod.Checked)
            {
                var code = pairingCode.Text.Replace("-", "", StringComparison.Ordinal).Trim();
                preview = await client.PreviewPairingAsync(code, lifetime.Token);
            }
            else
            {
                preview = await client.PreviewInvitationAsync(invitationCode.Text.Trim(),
                    employeeNo.Text.Trim(), password.Text, lifetime.Token);
            }
            status.Text = $"目標：{preview.DisplayName}。請確認後加入。";
            join.Enabled = true;
        });
    }

    private async Task JoinAsync()
    {
        await RunAsync(async () =>
        {
            if (preview is null) throw new InvalidOperationException("請先確認目標 Workspace。");
            var displayName = deviceName.Text.Trim();
            if (displayName.Length is < 1 or > 120) throw new InvalidOperationException("裝置名稱須為 1 到 120 個字元。");
            var endpoint = BaseUri().ToString();
            var pending = repository.Settings.CloudPendingDeviceJoin(settings);
            if (pending is not null && !string.Equals(pending.BaseUrl, endpoint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("另一個 Cloud 有未完成的裝置加入；請先返回原網址處理。");
            var attempt = pending is null ? CloudDeviceJoinAttempt.Create() : new CloudDeviceJoinAttempt(pending.DeviceToken);
            repository.Settings.SetCloudPendingDeviceJoin(settings, endpoint, displayName,
                attempt.DeviceToken, pending?.StartedAtUtc ?? DateTimeOffset.UtcNow);
            settings.CloudBaseUrl = endpoint;
            repository.Settings.Save(settings);
            CloudDeviceIdentity claimed;
            try
            {
                claimed = pairingMethod.Checked
                    ? await Client().ClaimPairingAsync(pairingCode.Text.Replace("-", "", StringComparison.Ordinal).Trim(),
                        displayName, Application.ProductVersion, attempt, lifetime.Token, directJoin: true)
                    : await Client().ClaimInvitationAsync(invitationCode.Text.Trim(), employeeNo.Text.Trim(),
                        password.Text, displayName, Application.ProductVersion, attempt, lifetime.Token);
                password.Clear();
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                          or CloudApiException { Code: "PAIRING_ALREADY_USED" or "OTP_ALREADY_USED" })
            {
                if (await TryRecoverAsync(endpoint, attempt.DeviceToken)) return;
                throw new InvalidOperationException("加入結果尚未確認，裝置憑證已保留。請重新開啟此流程，先嘗試找回裝置。", error);
            }
            if (claimed.WorkspaceId != preview.WorkspaceId)
                throw new InvalidDataException("Cloud 回傳的 Workspace 與確認的目標不一致。");
            await CompleteAsync(endpoint, attempt.DeviceToken, claimed.WorkspaceId, claimed.DeviceId);
        });
    }

    private async Task RecoverPendingAsync()
    {
        var pending = repository.Settings.CloudPendingDeviceJoin(settings);
        if (pending is null) return;
        url.Text = pending.BaseUrl;
        deviceName.Text = pending.DeviceDisplayName;
        await RunAsync(async () =>
        {
            if (!await TryRecoverAsync(pending.BaseUrl, pending.DeviceToken))
                status.Text = "先前加入尚未成功；裝置憑證已保留，請重新授權後繼續。";
        });
    }

    private async Task<bool> TryRecoverAsync(string endpoint, string token)
    {
        try
        {
            var identity = await new CloudClient(http, new Uri(endpoint), token).GetCurrentDeviceAsync(lifetime.Token);
            await CompleteAsync(endpoint, token, identity.WorkspaceId, identity.DeviceId);
            return true;
        }
        catch (CloudApiException error) when (error.StatusCode == HttpStatusCode.Unauthorized) { return false; }
    }

    private async Task CompleteAsync(string endpoint, string token, string workspaceId, string deviceId)
    {
        var client = new CloudClient(http, new Uri(endpoint), token);
        var identity = await client.GetCurrentDeviceAsync(lifetime.Token);
        if (identity.WorkspaceId != workspaceId || identity.DeviceId != deviceId)
            throw new InvalidDataException("裝置身分驗證結果不一致。");
        var authorityClient = new CloudEmployeeAuthorityClient(http, new Uri(endpoint), token);
        var authority = await authorityClient.GetStatusAsync(lifetime.Token);
        if (authority.State != "cloud" || authority.WorkspaceId != workspaceId || authority.DeviceId != deviceId)
            throw new InvalidDataException("中央帳號尚未準備好直接加入。");
        var snapshot = await authorityClient.GetSnapshotAsync(lifetime.Token);
        if (snapshot.WorkspaceId != workspaceId) throw new InvalidDataException("中央帳號 Workspace 不一致。");
        repository.CloudEmployees.ReplaceSnapshot(workspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
        settings.CloudBaseUrl = endpoint;
        settings.CloudWorkspaceId = workspaceId;
        settings.CloudDeviceId = deviceId;
        repository.Settings.SetCloudDeviceToken(settings, token);
        repository.Settings.ClearCloudPendingDeviceJoin(settings);
        repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
        repository.Settings.Save(settings);
        IdentityCompleted = true;
        MessageBox.Show(this, "裝置已加入，雲端員工帳號已同步。", "加入完成",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (busy) return;
        busy = true;
        authorize.Enabled = join.Enabled = false;
        UseWaitCursor = true;
        try { await work(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                status.Text = error.Message;
                MessageBox.Show(this, error.Message, "無法加入雲端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed) { UseWaitCursor = false; authorize.Enabled = true; join.Enabled = preview is not null; }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            http.Dispose();
        }
        base.Dispose(disposing);
    }
}
