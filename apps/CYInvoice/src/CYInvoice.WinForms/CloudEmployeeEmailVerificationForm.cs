using CYInvoice.Core.Cloud;

namespace CYInvoice.WinForms;

internal sealed class CloudEmployeeEmailVerificationForm : Form
{
    private readonly CloudEmployeeTransitionActionClient client;
    private readonly CloudEmployeeTransitionItem item;
    private readonly string snapshotHash;
    private readonly string? credentialVerifier;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label account = UiControls.Label(string.Empty);
    private readonly Label status = UiControls.Label("按「寄送驗證碼」後，驗證碼會寄到此帳號目前的 Email。");
    private readonly TextBox otp = UiControls.TextBox(6);
    private readonly Button send = UiControls.StandardButton("寄送驗證碼");
    private readonly Button verify = UiControls.StandardButton("完成驗證");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private CloudEmailChallenge? challenge;
    private bool busy;
    private bool resourcesDisposed;

    public CloudEmployeeEmailVerificationForm(
        CloudEmployeeTransitionActionClient client,
        CloudEmployeeTransitionItem item,
        string snapshotHash,
        string? credentialVerifier)
    {
        this.client = client;
        this.item = item;
        this.snapshotHash = snapshotHash;
        this.credentialVerifier = credentialVerifier;

        Text = "Cloud Employee Email 驗證";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 210);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        otp.TextAlign = HorizontalAlignment.Center;
        account.Text = $"{item.LocalEmployeeNo}  {item.LocalName}  {MaskEmail(item.LocalEmail)}";
        BuildLayout();
        UpdateActions();
    }

    public CloudEmployeeTransitionActionResult? Result { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        account.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(account, 0, 0);

        var otpRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        otpRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        otpRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        otpRow.Controls.Add(new Label
        {
            Text = "Email 驗證碼",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        otpRow.Controls.Add(otp, 1, 0);
        root.Controls.Add(otpRow, 0, 1);

        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = false;
        root.Controls.Add(status, 0, 2);

        send.Click += async (_, _) => await SendAsync();
        verify.Click += async (_, _) => await VerifyAsync();
        cancel.DialogResult = DialogResult.Cancel;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(verify);
        actions.Controls.Add(send);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private async Task SendAsync()
    {
        await RunBusyAsync(async () =>
        {
            challenge = await client.StartEmailVerificationAsync(item.LocalEmployeeNo, snapshotHash, lifetime.Token);
            status.Text = $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}。";
            otp.Focus();
        });
    }

    private async Task VerifyAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (challenge is null) throw new InvalidOperationException("請先寄送 Email 驗證碼。");
            var code = otp.Text.Trim();
            if (code.Length != 6 || !code.All(char.IsDigit))
                throw new InvalidOperationException("Email 驗證碼必須是 6 碼數字。");

            Result = await client.VerifyEmailAsync(
                item.LocalEmployeeNo,
                snapshotHash,
                challenge.ChallengeId,
                code,
                credentialVerifier,
                lifetime.Token);
            DialogResult = DialogResult.OK;
            Close();
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        UpdateActions();
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                MessageBox.Show(this, FriendlyMessage(error), "Email 驗證失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        send.Enabled = !busy;
        verify.Enabled = !busy && challenge is not null;
        otp.Enabled = !busy;
        cancel.Enabled = !busy;
    }

    private static string FriendlyMessage(Exception error)
    {
        if (error is not CloudApiException api) return error.Message;
        return api.Code switch
        {
            "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud Email 寄送服務尚未完成設定，帳號仍維持轉換中，不會提前切換權限。",
            "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
            "OTP_INVALID" => "Email 驗證碼錯誤。",
            "OTP_EXPIRED" => "Email 驗證碼已過期，請重新寄送。",
            "OTP_ATTEMPTS_EXCEEDED" => "Email 驗證碼錯誤次數已達上限，請重新寄送。",
            "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
            "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED" => "本機帳號資料已變更，請關閉視窗並重新執行帳號轉換檢查。",
            _ => $"Cloud API 錯誤：{api.Code}\n{api.Message}",
        };
    }

    private static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return "已登記 Email";
        var local = email[..at];
        var domain = email[(at + 1)..];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***@{domain}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
