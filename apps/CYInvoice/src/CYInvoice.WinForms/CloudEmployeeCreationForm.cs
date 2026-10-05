using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudEmployeeCreationForm : Form
{
    private readonly CloudEmployeeManagementClient client;
    private readonly EmployeeAccount actor;
    private readonly string actorPassword;
    private readonly CloudEmployeeCreateProposal proposal;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label account = UiControls.Label(string.Empty);
    private readonly Label status = UiControls.Label("新增 Cloud 使用者前，必須先驗證該使用者自己的 Email。");
    private readonly TextBox otp = UiControls.TextBox(6);
    private readonly Button send = UiControls.StandardButton("寄送驗證碼");
    private readonly Button create = UiControls.StandardButton("完成新增");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private CloudEmployeeCreateChallenge? challenge;
    private bool busy;
    private bool resourcesDisposed;

    public CloudEmployeeCreationForm(
        CloudEmployeeManagementClient client,
        EmployeeAccount actor,
        string actorPassword,
        CloudEmployeeCreateProposal proposal)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
        this.actorPassword = actorPassword ?? throw new ArgumentNullException(nameof(actorPassword));
        this.proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
        if (!EmployeeRoles.CanManageAccounts(actor.Role))
            throw new InvalidOperationException("只有管理員可以新增使用者。");

        Text = "新增雲端使用者－Email 驗證";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        otp.TextAlign = HorizontalAlignment.Center;
        account.Text = $"{proposal.EmployeeNo}  {proposal.Name}  {RoleText(proposal.Role)}  {MaskEmail(proposal.Email)}";
        BuildLayout();
        UpdateActions();
    }

    public CloudEmployeeTransitionIdentity? CreatedEmployee { get; private set; }

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

        send.Click += async (_, _) => await SendAsync();
        create.Click += async (_, _) => await CreateAsync();
        cancel.DialogResult = DialogResult.Cancel;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(create);
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
            challenge = await client.StartCreateAsync(actor.EmployeeNo, actorPassword, proposal, lifetime.Token);
            status.Text = $"驗證碼已寄至 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}。";
            otp.Focus();
        });
    }

    private async Task CreateAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (challenge is null) throw new InvalidOperationException("請先寄送新使用者的 Email 驗證碼。");
            var code = otp.Text.Trim();
            if (code.Length != 6 || !code.All(char.IsAsciiDigit))
                throw new InvalidOperationException("Email 驗證碼必須是 6 碼數字。");
            CreatedEmployee = await client.ConfirmCreateAsync(
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
                MessageBox.Show(this, FriendlyMessage(error), "新增使用者失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        create.Enabled = !busy && challenge is not null;
        otp.Enabled = !busy;
        cancel.Enabled = !busy;
    }

    private static string FriendlyMessage(Exception error)
    {
        if (error is not CloudApiException api) return error.Message;
        return api.Code switch
        {
            "MANAGER_REQUIRED" => "管理員帳密驗證失敗，或目前帳號已沒有帳號管理權限。",
            "EMPLOYEE_IDENTITY_EXISTS" => "員工編號或 Email 已存在於 Workspace，不能建立重複帳號。",
            "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud Email 寄送服務尚未完成設定，現在不能新增雲端使用者。",
            "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
            "OTP_INVALID" => "Email 驗證碼錯誤。",
            "OTP_EXPIRED" => "Email 驗證碼已過期，請重新寄送。",
            "OTP_ATTEMPTS_EXHAUSTED" => "Email 驗證碼錯誤次數已達上限，請重新開始新增流程。",
            "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
            _ => $"Cloud API 錯誤：{api.Code}\n{api.Message}",
        };
    }

    private static string RoleText(string role) => role == EmployeeRoles.Admin ? "管理員" : "一般使用者";

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
        if (Text != "新增雲端使用者－Email 驗證" || ShowIcon || AcceptButton is not null || CancelButton != cancel
            || send.Text != "寄送驗證碼" || create.Text != "完成新增" || otp.MaxLength != 6)
            throw new InvalidOperationException("新增雲端使用者 Email 驗證視窗配置不正確");
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
