using System.Net;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

/// <summary>
/// Explicit company-wide disaster recovery. Unlike ordinary Join this
/// operation removes ALL old active Device authorizations only when the new
/// Device is successfully committed by the server in one D1 transaction.
/// </summary>
internal sealed class CloudRecoveryForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    // CYID password, recovery code and pending Device Token must never follow a 307/308 redirect.
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox endpoint = UiControls.TextBox(240);
    private readonly TextBox code = UiControls.TextBox(40);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(16);
    private readonly TextBox deviceName = UiControls.TextBox(120);
    private readonly Label status = new()
    {
        Dock = DockStyle.Fill, Padding = new Padding(12),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Button previewButton = UiControls.StandardButton("驗證復原邀請");
    private readonly Button confirmButton = UiControls.StandardButton("確認接管並撤銷所有舊裝置");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private CloudRecoveryPreview? preview;
    private bool busy;
    private bool disposed;

    public bool IdentityCompleted { get; private set; }

    public CloudRecoveryForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "CYInvoice — 公司級裝置災難復原";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(750, 545);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        password.UseSystemPasswordChar = true;
        code.CharacterCasing = CharacterCasing.Lower;
        endpoint.Text = settings.CloudBaseUrl;
        deviceName.Text = Environment.MachineName[..Math.Min(Environment.MachineName.Length, 120)];

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        var introduction = new Label
        {
            Dock = DockStyle.Fill,
            Text = "此功能僅供遺失所有原裝置 Token 的公司進行災難復原。\n"
                + "復原邀請須由 CYWEB 已驗證的超級管理員簽發。\n"
                + "新電腦成功接管時，原 Workspace 的所有舊裝置會立即撤銷。"
                + " 只驗證邀請或寄送郵件不會撤銷。",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, 0, "Cloud API（HTTPS）", endpoint);
        AddField(fields, 1, "復原邀請碼", code);
        AddField(fields, 2, "CYID 超管員編", employeeNo);
        AddField(fields, 3, "CYID 密碼", password);
        AddField(fields, 4, "新裝置名稱", deviceName);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(2, 9, 2, 9),
        };
        confirmButton.Width = 265;
        previewButton.Width = 152;
        cancel.Width = 95;
        cancel.DialogResult = DialogResult.Cancel;
        confirmButton.Enabled = false;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(confirmButton);
        buttons.Controls.Add(previewButton);
        root.Controls.Add(introduction, 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(status, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = previewButton;
        CancelButton = cancel;

        foreach (var field in new[] { endpoint, code, employeeNo, password, deviceName })
            field.TextChanged += (_, _) => InvalidatePreview();
        previewButton.Click += async (_, _) => await RunAsync(PreviewAsync);
        confirmButton.Click += async (_, _) => await RunAsync(ClaimAsync);
        var pending = repository.Settings.CloudPendingRecovery(settings);
        if (pending is not null)
        {
            endpoint.Text = pending.BaseUrl;
            deviceName.Text = pending.DeviceDisplayName;
            status.Text = "偵測到先前未完成的災難復原：Pending Device Token 已加密保存。"
                + " 請使用原邀請碼與相同 Cloud 網址再次驗證；不會產生另一個 Token。";
        }
        else
        {
            status.Text = "請先驗證邀請與 Workspace。下一步會再要求明確確認全數撤銷。";
        }
    }

    private static void AddField(TableLayoutPanel panel, int row, string name, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Text = name, TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }
    private void InvalidatePreview()
    {
        preview = null;
        confirmButton.Enabled = false;
    }
    private Uri BaseUri()
    {
        if (!Uri.TryCreate(endpoint.Text.Trim(), UriKind.Absolute, out var url))
            throw new InvalidOperationException("請填入有效的 Cloud API HTTPS 網址。");
        return url;
    }
    private async Task PreviewAsync()
    {
        var client = new CloudRecoveryClient(http, BaseUri());
        var result = await client.PreviewAsync(
            code.Text.Trim(), employeeNo.Text.Trim(), password.Text, lifetime.Token);
        var pending = repository.Settings.CloudPendingRecovery(settings);
        if (pending is not null && (
            pending.BaseUrl != clientBaseUrl(BaseUri()) ||
            pending.WorkspaceId != result.WorkspaceId ||
            pending.IdentityWorkspaceId != result.IdentityWorkspaceId ||
            pending.InvitationId != result.InvitationId ||
            pending.DeviceDisplayName != deviceName.Text.Trim()))
            throw new InvalidOperationException(
                "待完成的復原憑證屬於其他網址、Workspace、邀請或裝置名稱；"
                + "不得重新產生 Token，請使用原本的復原內容。");
        preview = result;
        status.Text = $"復原目標已確認：{result.WorkspaceDisplayName}（{result.WorkspaceId}）。\n"
            + $"邀請到期：{result.ExpiresAt:yyyy-MM-dd HH:mm:ss zzz}。"
            + " 確認接管後，所有其他裝置會在同一筆交易中撤銷。";
        confirmButton.Enabled = true;
    }
    private async Task ClaimAsync()
    {
        var confirmed = preview ?? throw new InvalidOperationException("請先驗證復原邀請。");
        if (MessageBox.Show(this,
            $"即將在新裝置接管「{confirmed.WorkspaceDisplayName}」後，立即撤銷此 Workspace 的所有舊裝置。\n"
            + "舊 Token 將失效，舊電腦再次連線時可能執行本機資料清除。\n"
            + "此操作不是一般新增裝置。確定要執行公司級災難復原？",
            "最後確認：全數撤銷舊裝置", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var baseUrl = clientBaseUrl(BaseUri());
        var pending = repository.Settings.CloudPendingRecovery(settings);
        if (pending is null)
        {
            var token = CloudDeviceJoinAttempt.Create().DeviceToken;
            repository.Settings.SetCloudPendingRecovery(settings,
                baseUrl, confirmed.WorkspaceId, confirmed.IdentityWorkspaceId,
                confirmed.InvitationId, deviceName.Text.Trim(), token, DateTimeOffset.UtcNow);
            repository.Settings.Save(settings); // persisted BEFORE any network mutation
            pending = repository.Settings.CloudPendingRecovery(settings)
                ?? throw new InvalidOperationException("Pending Token 無法持久保存，未送出復原。");
        }
        else if (pending.BaseUrl != baseUrl || pending.WorkspaceId != confirmed.WorkspaceId ||
                 pending.IdentityWorkspaceId != confirmed.IdentityWorkspaceId ||
                 pending.InvitationId != confirmed.InvitationId)
            throw new InvalidOperationException("先前的 Pending Recovery scope 不一致。");

        CloudRecoveryClaim result;
        try
        {
            result = await new CloudRecoveryClient(http, BaseUri()).ClaimAsync(
                confirmed, pending, code.Text.Trim(), employeeNo.Text.Trim(),
                password.Text, ApplicationVersion.ReadDisplay(), lifetime.Token);
        }
        catch
        {
            // A timeout can occur AFTER all old Devices were revoked.
            // Persist and reuse the original pending Token on the next attempt.
            status.Text = "復原結果尚未確認：Pending Device Token 已安全保留。"
                + " 請勿重建 Token 或發出新的復原邀請，使用原邀請重開此頁查回。";
            throw;
        }
        finally { password.Clear(); }
        await CommitAsync(result, pending);
    }
    private async Task CommitAsync(CloudRecoveryClaim claim, CloudPendingRecoveryState pending)
    {
        var baseUri = new Uri(pending.BaseUrl);
        var current = await new CloudClient(http, baseUri, pending.DeviceToken)
            .GetCurrentDeviceAsync(lifetime.Token);
        if (current.WorkspaceId != pending.WorkspaceId || current.DeviceId != claim.DeviceId)
            throw new InvalidDataException("已復原 Device 與原 Workspace／Pending Token 驗證不一致。");
        var binding = await CyIdGateway.DiscoverAsync(http, baseUri, pending.DeviceToken,
            current.WorkspaceId, current.DeviceId, lifetime.Token);
        if (binding is null || binding.IdentityWorkspaceId != pending.IdentityWorkspaceId
            || binding.ApplicationId != "CYINVOICE")
            throw new InvalidDataException("復原裝置的 CYID 綁定與原邀請驗證結果不一致。");
        settings.CloudBaseUrl = pending.BaseUrl;
        settings.CloudWorkspaceId = current.WorkspaceId;
        settings.CloudDeviceId = current.DeviceId;
        repository.Settings.SetCloudDeviceToken(settings, pending.DeviceToken);
        repository.Settings.ClearCloudPendingRecovery(settings);
        repository.Settings.ConfirmCyIdConfiguration(settings, binding);
        repository.Settings.Save(settings);
        IdentityCompleted = true;
        DialogResult = DialogResult.OK;
        MessageBox.Show(this, "復原已成功，新裝置已加入，所有舊裝置均已撤銷。",
            "復原完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
    private static string clientBaseUrl(Uri uri) => uri.AbsoluteUri.TrimEnd('/') + "/";
    private async Task RunAsync(Func<Task> callback)
    {
        if (busy) return;
        busy = true;
        previewButton.Enabled = false;
        confirmButton.Enabled = false;
        cancel.Enabled = false;
        UseWaitCursor = true;
        try { await callback(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                status.Text = error.Message;
                MessageBox.Show(this, error.Message, "復原尚未完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                previewButton.Enabled = true;
                confirmButton.Enabled = preview is not null;
                cancel.Enabled = true;
                UseWaitCursor = false;
            }
        }
    }
    internal void VerifySmokeLayout()
    {
        if (!Text.Contains("災難復原", StringComparison.Ordinal)
            || confirmButton.Enabled
            || !confirmButton.Text.Contains("撤銷所有舊裝置", StringComparison.Ordinal))
            throw new InvalidOperationException("復原介面必須預設關閉不可逆接管動作。");
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            http.Dispose();
        }
        base.Dispose(disposing);
    }
}
