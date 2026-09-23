using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudPasswordRecoveryForm : Form
{
    private readonly LocalRepository repository;
    private readonly CloudEmployeeAccountClient accountClient;
    private readonly CloudEmployeeAuthorityClient authorityClient;
    private readonly string workspaceId;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox otp = UiControls.TextBox(6);
    private readonly TextBox newPassword = UiControls.TextBox(200);
    private readonly TextBox confirmPassword = UiControls.TextBox(200);
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button send = UiControls.StandardButton("寄送驗證碼");
    private readonly Button reset = UiControls.StandardButton("重設密碼");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private CloudEmployeePasswordRecoveryChallenge? challenge;
    private bool busy;

    public CloudPasswordRecoveryForm(LocalRepository repository, HttpClient httpClient)
    {
        this.repository = repository;
        var settings = repository.Settings.LoadOrCreate();
        var token = repository.Settings.CloudDeviceToken(settings);
        if (!repository.UsesCloudEmployeeAuthority() || token.Length == 0 || settings.CloudBaseUrl.Length == 0)
            throw new InvalidOperationException("雲端密碼復原需要已完成帳號切換、有效的裝置身分與雲端連線。");
        workspaceId = settings.CloudWorkspaceId;
        var baseUri = new Uri(settings.CloudBaseUrl, UriKind.Absolute);
        accountClient = new CloudEmployeeAccountClient(httpClient, baseUri, token);
        authorityClient = new CloudEmployeeAuthorityClient(httpClient, baseUri, token);

        Text = "雲端密碼復原";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        newPassword.UseSystemPasswordChar = true;
        confirmPassword.UseSystemPasswordChar = true;
        otp.TextAlign = HorizontalAlignment.Center;
        status.Text = "輸入員工編號，驗證碼會寄至雲端帳號已驗證的 Email。";
        BuildLayout();
        UpdateActions();
        Shown += (_, _) => employeeNo.Focus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(status, 0, 0);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        AddField(fields, "員工編號", employeeNo, 0);
        AddField(fields, "Email 驗證碼", otp, 1);
        AddField(fields, "新密碼", newPassword, 2);
        AddField(fields, "再次輸入", confirmPassword, 3);
        root.Controls.Add(fields, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        send.Click += async (_, _) => await SendAsync();
        reset.Click += async (_, _) => await ResetAsync();
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(cancel);
        actions.Controls.Add(reset);
        actions.Controls.Add(send);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel fields, string label, TextBox input, int row)
    {
        fields.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(3, 8, 3, 8);
        fields.Controls.Add(input, 1, row);
    }

    private async Task SendAsync()
    {
        if (busy) return;
        if (employeeNo.Text.Trim().Length != 4 || !employeeNo.Text.Trim().All(char.IsAsciiDigit))
        {
            ShowError("請輸入 4 碼員工編號。");
            return;
        }
        await RunBusyAsync(async () =>
        {
            challenge = await accountClient.StartPasswordRecoveryAsync(employeeNo.Text.Trim(), lifetime.Token);
            employeeNo.ReadOnly = true;
            status.Text = $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm}。";
            otp.Focus();
        });
    }

    private async Task ResetAsync()
    {
        if (busy) return;
        if (challenge is null) { ShowError("請先寄送 Email 驗證碼。"); return; }
        if (otp.Text.Trim().Length != 6 || !otp.Text.Trim().All(char.IsAsciiDigit))
        { ShowError("請輸入 6 碼 Email 驗證碼。"); return; }
        if (newPassword.Text != confirmPassword.Text)
        { ShowError("兩次輸入的新密碼不一致。"); return; }

        await RunBusyAsync(async () =>
        {
            await accountClient.ConfirmPasswordRecoveryAsync(
                employeeNo.Text.Trim(), challenge.ChallengeId, otp.Text.Trim(), newPassword.Text, lifetime.Token);
            try
            {
                var snapshot = await authorityClient.GetSnapshotAsync(lifetime.Token);
                if (!string.Equals(snapshot.WorkspaceId, workspaceId, StringComparison.Ordinal))
                    throw new InvalidDataException("雲端帳號快取回傳的 Workspace 不一致。");
                repository.CloudEmployees.ReplaceSnapshot(snapshot.WorkspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
            }
            catch
            {
                repository.CloudEmployees.DisableOfflineCredential(employeeNo.Text.Trim());
                MessageBox.Show(this,
                    "中央密碼已重設，但本機帳號快取更新失敗，這個帳號的舊離線密碼已停用。請保持連線並重新啟動 CYInvoice 以同步新密碼。",
                    "密碼已重設", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            MessageBox.Show(this, "雲端密碼已重設，這台電腦的帳號快取也已更新。",
                "密碼已重設", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        busy = true;
        UseWaitCursor = true;
        UpdateActions();
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception problem) { if (!IsDisposed) ShowError(FriendlyMessage(problem)); }
        finally
        {
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
                UpdateActions();
            }
        }
    }

    private void UpdateActions()
    {
        send.Enabled = !busy && challenge is null;
        reset.Enabled = !busy && challenge is not null;
        cancel.Enabled = !busy;
        employeeNo.Enabled = !busy;
        otp.Enabled = !busy;
        newPassword.Enabled = !busy;
        confirmPassword.Enabled = !busy;
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "無法復原密碼", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static string FriendlyMessage(Exception problem)
    {
        if (problem is not CloudApiException api) return problem.Message;
        return api.Code switch
        {
            "RECOVERY_ACCOUNT_NOT_FOUND" => "找不到可復原的已啟用雲端帳號，或 Email 尚未驗證。",
            "OTP_INVALID" => "Email 驗證碼錯誤。",
            "OTP_EXPIRED" => "Email 驗證碼已過期，請重新開啟復原視窗。",
            "OTP_ALREADY_USED" => "這組驗證碼已使用，請重新開啟復原視窗。",
            "OTP_ATTEMPTS_EXHAUSTED" => "驗證碼錯誤次數已達上限，請重新開啟復原視窗。",
            "OTP_RESEND_COOLDOWN" or "OTP_RATE_LIMITED" => "驗證碼寄送過於頻繁，請稍後再試。",
            "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
            "EMAIL_PROVIDER_NOT_CONFIGURED" or "OTP_NOT_CONFIGURED" => "雲端 Email 驗證服務尚未完成設定。",
            "RECOVERY_STATE_CHANGED" => "帳號狀態已變更，請重新開始密碼復原。",
            "NOT_FOUND" => "目前連線的雲端服務尚未更新密碼復原功能，請先更新 development Worker。",
            "UNAUTHORIZED" => "這台電腦的雲端裝置驗證失敗，請先檢查雲端連線。",
            _ => $"雲端密碼復原失敗：{api.Code}",
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
