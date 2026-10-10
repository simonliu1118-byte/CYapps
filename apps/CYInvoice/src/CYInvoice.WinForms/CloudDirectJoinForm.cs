using System.Net;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed partial class CloudDirectJoinForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly HttpClient http;
    private readonly Action<string, string, MessageBoxIcon> showMessage;
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

    private readonly Panel inputPage = new() { Dock = DockStyle.Fill };
    private readonly Panel confirmationPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Button next = UiControls.StandardButton("下一步");
    private readonly Button confirmJoin = UiControls.StandardButton("確認加入");
    private readonly Button back = UiControls.StandardButton("上一步");
    private readonly Button inputCancel = UiControls.StandardButton("取消");
    private readonly Button confirmCancel = UiControls.StandardButton("取消");
    private readonly FlowLayoutPanel inputActions = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
    };
    private readonly FlowLayoutPanel confirmationActions = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
    };

    private readonly Label confirmWorkspace = ConfirmationValue();
    private readonly Label confirmWorkspaceId = ConfirmationValue();
    private readonly Label confirmEndpoint = ConfirmationValue();
    private readonly Label confirmMethod = ConfirmationValue();
    private readonly Label confirmAdmin = ConfirmationValue();
    private readonly Label confirmDevice = ConfirmationValue();

    private CloudWorkspacePreview? preview;
    private bool busy;
    private bool resourcesDisposed;

    public CloudDirectJoinForm(LocalRepository repository)
        : this(repository, new HttpClient(), null)
    {
    }

    private CloudDirectJoinForm(LocalRepository repository, HttpClient httpClient,
        Action<string, string, MessageBoxIcon>? notification)
    {
        this.repository = repository;
        http = httpClient;
        showMessage = notification ?? ((message, title, icon) =>
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon));
        settings = repository.Settings.LoadOrCreate();

        Text = "首次開啟：直接加入雲端";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 430);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
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
        password.TextChanged += (_, _) => ResetAuthorization();
        deviceName.TextChanged += (_, _) => ResetAuthorization();

        next.Click += async (_, _) => await NextAsync();
        confirmJoin.Click += async (_, _) => await JoinAsync();
        back.Click += (_, _) => ShowInputPage(focus: true);

        ChangeMethod();
        ShowInputPage(focus: false);
        Shown += async (_, _) =>
        {
            FocusFirstInput();
            await RecoverPendingAsync();
        };
    }

    public bool IdentityCompleted { get; private set; }

    private void BuildLayout()
    {
        inputCancel.DialogResult = DialogResult.Cancel;
        confirmCancel.DialogResult = DialogResult.Cancel;
        next.Width = 110;
        confirmJoin.Width = 120;
        back.Width = 100;
        inputCancel.Width = confirmCancel.Width = 90;

        var root = new Panel { Dock = DockStyle.Fill };
        BuildInputPage();
        BuildConfirmationPage();
        root.Controls.Add(confirmationPage);
        root.Controls.Add(inputPage);
        Controls.Add(root);
    }

    private void BuildInputPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 7; row++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
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
        root.Controls.Add(status, 1, 8);

        inputActions.Controls.Add(inputCancel);
        inputActions.Controls.Add(next);
        root.Controls.Add(inputActions, 1, 9);
        inputPage.Controls.Add(root);
    }

    private void BuildConfirmationPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        for (var row = 1; row <= 6; row++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var title = new Label
        {
            Text = "請確認要加入的 Workspace",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
        };
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);

        AddConfirmation(root, 1, "Workspace", confirmWorkspace);
        AddConfirmation(root, 2, "Workspace ID", confirmWorkspaceId);
        AddConfirmation(root, 3, "Cloud API", confirmEndpoint);
        AddConfirmation(root, 4, "加入方式", confirmMethod);
        AddConfirmation(root, 5, "授權身分", confirmAdmin);
        AddConfirmation(root, 6, "這台裝置名稱", confirmDevice);

        var note = new Label
        {
            Text = "確認後才會正式建立這台 Device 並加入 Workspace。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray,
        };
        root.Controls.Add(note, 1, 8);

        confirmationActions.Controls.Add(confirmCancel);
        confirmationActions.Controls.Add(back);
        confirmationActions.Controls.Add(confirmJoin);
        root.Controls.Add(confirmationActions, 1, 9);
        confirmationPage.Controls.Add(root);
    }

    internal void VerifySmokeLayout()
    {
        if (ClientSize.Width > 620 || ClientSize.Height > 450)
            throw new InvalidOperationException("雲端加入視窗尺寸異常");
        VerifyActions(inputActions, "input");
        VerifyActions(confirmationActions, "confirmation");
        if (next.Text != "下一步" || inputCancel.Text != "取消"
            || confirmJoin.Text != "確認加入" || back.Text != "上一步" || confirmCancel.Text != "取消")
            throw new InvalidOperationException("雲端加入流程按鈕文字異常");
    }

    private static void VerifyActions(FlowLayoutPanel actions, string page)
    {
        var requiredWidth = actions.Controls.Cast<Control>()
            .Sum(control => control.Width + control.Margin.Horizontal);
        if (requiredWidth > 420)
            throw new InvalidOperationException($"Cloud direct-join {page} action row is too wide: {requiredWidth}");

        if (!actions.Visible || actions.ClientSize.Width <= 0 || actions.ClientSize.Height <= 0)
            return;

        actions.PerformLayout();
        foreach (Control button in actions.Controls)
        {
            if (button.Left < 0 || button.Top < 0 || button.Right > actions.ClientSize.Width
                || button.Bottom > actions.ClientSize.Height)
                throw new InvalidOperationException($"Cloud direct-join {page} action button is clipped");
        }
    }

    private static Label ConfirmationValue() => new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(6, 0, 6, 0),
        BackColor = Color.White,
    };

    private static void AddField(TableLayoutPanel root, int row, string name, Control field)
    {
        root.Controls.Add(new Label
        {
            Text = name,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        field.Dock = DockStyle.Fill;
        root.Controls.Add(field, 1, row);
    }

    private static void AddConfirmation(TableLayoutPanel root, int row, string name, Control value)
    {
        root.Controls.Add(new Label
        {
            Text = name,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        root.Controls.Add(value, 1, row);
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
        confirmJoin.Enabled = false;
        status.Text = "輸入完成後按「下一步」確認 Workspace。";
    }

    private void ShowInputPage(bool focus)
    {
        confirmationPage.Visible = false;
        inputPage.Visible = true;
        inputPage.BringToFront();
        AcceptButton = next;
        CancelButton = inputCancel;
        if (focus && IsHandleCreated)
            BeginInvoke((Action)FocusFirstInput);
    }

    private void ShowConfirmationPage()
    {
        FillConfirmation();
        inputPage.Visible = false;
        confirmationPage.Visible = true;
        confirmationPage.BringToFront();
        AcceptButton = confirmJoin;
        CancelButton = confirmCancel;
        if (IsHandleCreated)
            BeginInvoke((Action)(() => confirmJoin.Focus()));
    }

    private void FocusFirstInput()
    {
        if (IsDisposed) return;
        if (string.IsNullOrWhiteSpace(url.Text)) url.Focus();
        else if (pairingMethod.Checked && string.IsNullOrWhiteSpace(pairingCode.Text)) pairingCode.Focus();
        else if (ownerMethod.Checked && string.IsNullOrWhiteSpace(invitationCode.Text)) invitationCode.Focus();
        else if (ownerMethod.Checked && string.IsNullOrWhiteSpace(employeeNo.Text)) employeeNo.Focus();
        else if (ownerMethod.Checked && string.IsNullOrEmpty(password.Text)) password.Focus();
        else deviceName.Focus();
    }

    private void FillConfirmation()
    {
        if (preview is null) return;
        confirmWorkspace.Text = preview.DisplayName;
        confirmWorkspaceId.Text = preview.WorkspaceId;
        confirmEndpoint.Text = BaseUri().ToString();
        confirmMethod.Text = pairingMethod.Checked ? "配對碼" : "邀請碼＋超管帳密";
        confirmAdmin.Text = pairingMethod.Checked ? "由配對碼授權" : $"SUPER_ADMIN / {employeeNo.Text.Trim()}";
        confirmDevice.Text = deviceName.Text.Trim();
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

    private void ValidateInput()
    {
        _ = BaseUri();
        var displayName = deviceName.Text.Trim();
        if (displayName.Length is < 1 or > 120)
            throw new InvalidOperationException("裝置名稱須為 1 到 120 個字元。");

        if (pairingMethod.Checked)
        {
            if (string.IsNullOrWhiteSpace(pairingCode.Text))
                throw new InvalidOperationException("請輸入配對碼。");
            return;
        }

        if (string.IsNullOrWhiteSpace(invitationCode.Text))
            throw new InvalidOperationException("請輸入邀請碼。");
        if (string.IsNullOrWhiteSpace(employeeNo.Text))
            throw new InvalidOperationException("請輸入超管員工編號。");
        if (string.IsNullOrEmpty(password.Text))
            throw new InvalidOperationException("請輸入超管密碼。");
    }

    private async Task NextAsync()
    {
        await RunAsync(async () =>
        {
            ValidateInput();
            var client = Client();
            var health = await client.CheckHealthAsync(lifetime.Token);
            var problem = CloudCompatibility.Problem(health);
            if (problem.Length != 0) throw new InvalidOperationException(problem);

            preview = pairingMethod.Checked
                ? await client.PreviewPairingAsync(
                    pairingCode.Text.Replace("-", "", StringComparison.Ordinal).Trim(), lifetime.Token)
                : await client.PreviewInvitationAsync(
                    invitationCode.Text.Trim(), employeeNo.Text.Trim(), password.Text, lifetime.Token);

            ShowConfirmationPage();
        });
    }

    private async Task JoinAsync()
    {
        await RunAsync(async () =>
        {
            // TextChanged also invalidates the editable preview when a successful
            // claim clears the password. Keep the confirmed target for this operation.
            var confirmedWorkspace = preview
                ?? throw new InvalidOperationException("請先完成 Workspace 確認。");
            ValidateInput();
            var displayName = deviceName.Text.Trim();
            var endpoint = BaseUri().ToString();
            var pending = repository.Settings.CloudPendingDeviceJoin(settings);
            if (pending is not null && !string.Equals(pending.BaseUrl, endpoint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("另一個 Cloud 有未完成的裝置加入；請先返回原網址處理。");

            var attempt = pending is null
                ? CloudDeviceJoinAttempt.Create()
                : new CloudDeviceJoinAttempt(pending.DeviceToken);
            repository.Settings.SetCloudPendingDeviceJoin(
                settings,
                endpoint,
                displayName,
                attempt.DeviceToken,
                pending?.StartedAtUtc ?? DateTimeOffset.UtcNow);
            settings.CloudBaseUrl = endpoint;
            repository.Settings.Save(settings);

            CloudDeviceIdentity claimed;
            try
            {
                claimed = pairingMethod.Checked
                    ? await Client().ClaimPairingAsync(
                        pairingCode.Text.Replace("-", "", StringComparison.Ordinal).Trim(),
                        displayName,
                        ApplicationVersion.ReadDisplay(),
                        attempt,
                        lifetime.Token,
                        directJoin: true)
                    : await Client().ClaimInvitationAsync(
                        invitationCode.Text.Trim(),
                        employeeNo.Text.Trim(),
                        password.Text,
                        displayName,
                        ApplicationVersion.ReadDisplay(),
                        attempt,
                        lifetime.Token);
                password.Clear();
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                          or CloudApiException { Code: "PAIRING_ALREADY_USED" or "OTP_ALREADY_USED" })
            {
                if (await TryRecoverAsync(endpoint, attempt.DeviceToken)) return;
                throw new InvalidOperationException(
                    "加入結果尚未確認，裝置憑證已保留。請重新開啟此流程，先嘗試找回裝置。",
                    error);
            }

            if (claimed.WorkspaceId != confirmedWorkspace.WorkspaceId)
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
            {
                ShowInputPage(focus: true);
                status.Text = "先前加入尚未成功；裝置憑證已保留，請重新授權後繼續。";
            }
        });
    }

    private async Task<bool> TryRecoverAsync(string endpoint, string token)
    {
        try
        {
            var identity = await new CloudClient(http, new Uri(endpoint), token)
                .GetCurrentDeviceAsync(lifetime.Token);
            await CompleteAsync(endpoint, token, identity.WorkspaceId, identity.DeviceId);
            return true;
        }
        catch (CloudApiException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false;
        }
    }

    private async Task CompleteAsync(string endpoint, string token, string workspaceId, string deviceId)
    {
        var client = new CloudClient(http, new Uri(endpoint), token);
        var identity = await client.GetCurrentDeviceAsync(lifetime.Token);
        if (identity.WorkspaceId != workspaceId || identity.DeviceId != deviceId)
            throw new InvalidDataException("裝置身分驗證結果不一致。");

        var binding = await CyIdGateway.DiscoverAsync(http, new Uri(endpoint), token, workspaceId, deviceId, lifetime.Token);
        if (binding is not null)
        {
            settings.CloudBaseUrl = endpoint;
            settings.CloudWorkspaceId = workspaceId;
            settings.CloudDeviceId = deviceId;
            repository.Settings.SetCloudDeviceToken(settings, token);
            repository.Settings.ConfirmCyIdConfiguration(settings, binding);
            repository.Settings.ClearCloudPendingDeviceJoin(settings);
            repository.Settings.Save(settings);
            IdentityCompleted = true;
            showMessage("裝置已加入，員工驗證由 CYID 提供。", "加入完成", MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var authorityClient = new CloudEmployeeAuthorityClient(http, new Uri(endpoint), token);
        var authority = await authorityClient.GetStatusAsync(lifetime.Token);
        if (authority.State != "cloud" || authority.WorkspaceId != workspaceId || authority.DeviceId != deviceId)
            throw new InvalidDataException("中央帳號尚未準備好直接加入。");

        var snapshot = await authorityClient.GetSnapshotAsync(lifetime.Token);
        if (snapshot.WorkspaceId != workspaceId)
            throw new InvalidDataException("中央帳號 Workspace 不一致。");

        repository.CloudEmployees.ReplaceSnapshot(workspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
        settings.CloudBaseUrl = endpoint;
        settings.CloudWorkspaceId = workspaceId;
        settings.CloudDeviceId = deviceId;
        repository.Settings.SetCloudDeviceToken(settings, token);
        repository.Settings.ClearCloudPendingDeviceJoin(settings);
        repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
        repository.Settings.Save(settings);

        IdentityCompleted = true;
        showMessage("裝置已加入，雲端員工帳號已同步。", "加入完成", MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (busy) return;
        busy = true;
        SetBusy(true);
        try
        {
            await work();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                status.Text = error.Message;
                showMessage(error.Message, "無法加入雲端", MessageBoxIcon.Warning);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        UseWaitCursor = value;
        next.Enabled = !value;
        confirmJoin.Enabled = !value && preview is not null;
        back.Enabled = !value;
        inputCancel.Enabled = !value;
        confirmCancel.Enabled = !value;
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
