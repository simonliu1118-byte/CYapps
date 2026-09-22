using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudEmployeeUpdateVerificationForm : Form
{
    private readonly CloudEmployeeAccountClient client;
    private readonly EmployeeAccount actor;
    private readonly string actorPassword;
    private readonly CloudEmployeeUpdateProposal proposal;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label account = UiControls.Label(string.Empty);
    private readonly Label status = UiControls.Label(string.Empty);
    private readonly TextBox otp = UiControls.TextBox(6);
    private readonly Button resend = UiControls.StandardButton("重新寄送");
    private readonly Button confirm = UiControls.StandardButton("完成修改");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private CloudEmployeeUpdateChallenge challenge;
    private bool busy;
    private bool resourcesDisposed;

    public CloudEmployeeUpdateVerificationForm(
        CloudEmployeeAccountClient client,
        EmployeeAccount actor,
        string actorPassword,
        CloudEmployeeUpdateProposal proposal,
        CloudEmployeeUpdateChallenge challenge)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
        this.actorPassword = actorPassword ?? throw new ArgumentNullException(nameof(actorPassword));
        this.proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
        this.challenge = challenge ?? throw new ArgumentNullException(nameof(challenge));

        Text = "修改使用者－Email 驗證";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        otp.TextAlign = HorizontalAlignment.Center;
        account.Text = $"{proposal.TargetEmployeeNo}  {proposal.Name}  {MaskEmail(proposal.Email)}";
        status.Text = $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}。";
        BuildLayout();
        UpdateActions();
    }

    public CloudEmployeeTransitionIdentity? UpdatedEmployee { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
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

        resend.Click += async (_, _) => await ResendAsync();
        confirm.Click += async (_, _) => await ConfirmAsync();
        cancel.DialogResult = DialogResult.Cancel;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(confirm);
        actions.Controls.Add(resend);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private async Task ResendAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await client.StartUpdateAsync(actor.EmployeeNo, actorPassword, proposal, lifetime.Token);
            if (!result.VerificationRequired || result.Challenge is null)
            {
                UpdatedEmployee = result.Employee;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            challenge = result.Challenge;
            status.Text = $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}。";
            otp.Clear();
            otp.Focus();
        });
    }

    private async Task ConfirmAsync()
    {
        await RunBusyAsync(async () =>
        {
            var code = otp.Text.Trim();
            if (code.Length != 6 || !code.All(char.IsAsciiDigit))
                throw new InvalidOperationException("Email 驗證碼必須是 6 碼數字。");
            UpdatedEmployee = await client.ConfirmUpdateAsync(
                actor.EmployeeNo,
                actorPassword,
                proposal,
                challenge.ChallengeId,
                code,
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
                MessageBox.Show(this, FriendlyMessage(error), "修改使用者失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        resend.Enabled = !busy;
        confirm.Enabled = !busy;
        otp.Enabled = !busy;
        cancel.Enabled = !busy;
    }

    private static string FriendlyMessage(Exception error)
    {
        if (error is not CloudApiException api) return error.Message;
        return api.Code switch
        {
            "MANAGER_REQUIRED" => "管理員帳密驗證失敗，或目前帳號已沒有帳號管理權限。",
            "EMPLOYEE_EMAIL_EXISTS" => "這個 Email 已被 Workspace 內其他使用者使用。",
            "SUPER_ADMIN_SELF_REQUIRED" => "超級管理員資料只能由超級管理員本人修改。",
            "SUPER_ADMIN_TRANSFER_REQUIRED" => "超級管理員權限不可在一般修改資料流程中變更，請使用移交超管權限。",
            "SELF_ROLE_CHANGE_FORBIDDEN" => "管理員不可在修改資料時變更自己的權限。",
            "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud Email 寄送服務尚未完成設定，目前不能變更 Email。",
            "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
            "OTP_INVALID" => "Email 驗證碼錯誤。",
            "OTP_EXPIRED" => "Email 驗證碼已過期，請重新寄送。",
            "OTP_ATTEMPTS_EXHAUSTED" => "Email 驗證碼錯誤次數已達上限，請重新開始修改流程。",
            "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
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

    internal void VerifySmokeLayout()
    {
        if (Text != "修改使用者－Email 驗證" || ShowIcon || AcceptButton is not null || CancelButton != cancel
            || resend.Text != "重新寄送" || confirm.Text != "完成修改" || otp.MaxLength != 6)
            throw new InvalidOperationException("修改使用者 Email 驗證視窗配置不正確");
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
