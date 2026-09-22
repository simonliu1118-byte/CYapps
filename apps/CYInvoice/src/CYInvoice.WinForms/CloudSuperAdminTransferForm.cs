using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudSuperAdminTransferForm : Form
{
    private readonly CloudSuperAdminTransferClient client;
    private readonly EmployeeAccount actor;
    private readonly string actorPassword;
    private readonly EmployeeAccount target;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label targetLabel = UiControls.Label(string.Empty);
    private readonly Label status = UiControls.Label("必須再次驗證目前超級管理員的 Email，才能完成權限移交。");
    private readonly TextBox otp = UiControls.TextBox(6);
    private readonly Button send = UiControls.StandardButton("寄送驗證碼");
    private readonly Button confirm = UiControls.DangerButton("確認移交");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private CloudSuperAdminTransferChallenge? challenge;
    private bool busy;
    private bool resourcesDisposed;

    public CloudSuperAdminTransferForm(
        CloudSuperAdminTransferClient client,
        EmployeeAccount actor,
        string actorPassword,
        EmployeeAccount target)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
        this.actorPassword = actorPassword ?? throw new ArgumentNullException(nameof(actorPassword));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        if (actor.Role != EmployeeRoles.SuperAdmin)
            throw new InvalidOperationException("只有目前超級管理員可以移交超管權限。");
        if (target.Role != EmployeeRoles.Admin || !target.Enabled)
            throw new InvalidOperationException("接任者必須是啟用中的管理員。");

        Text = "移交超級管理員權限";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        targetLabel.Text = $"接任者：{target.EmployeeNo}  {target.Name}  {MaskEmail(target.Email)}";
        otp.TextAlign = HorizontalAlignment.Center;
        BuildLayout();
        UpdateActions();
    }

    public CloudSuperAdminTransferResult? Result { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        targetLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(targetLabel, 0, 0);
        root.Controls.Add(new Label
        {
            Text = $"目前超級管理員：{actor.EmployeeNo}  {actor.Name}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);

        var otpRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        otpRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        otpRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        otpRow.Controls.Add(new Label
        {
            Text = "X 的驗證碼",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        otpRow.Controls.Add(otp, 1, 0);
        root.Controls.Add(otpRow, 0, 2);

        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = false;
        root.Controls.Add(status, 0, 3);

        send.Click += async (_, _) => await SendAsync();
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
        actions.Controls.Add(send);
        root.Controls.Add(actions, 0, 4);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private async Task SendAsync()
    {
        await RunBusyAsync(async () =>
        {
            challenge = await client.StartAsync(
                actor.EmployeeNo,
                actorPassword,
                target.EmployeeNo,
                lifetime.Token);
            status.Text = $"驗證碼已寄至目前超管 {challenge.MaskedEmail}，有效至 {challenge.ExpiresAt.ToLocalTime():HH:mm:ss}。";
            otp.Focus();
        });
    }

    private async Task ConfirmAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (challenge is null) throw new InvalidOperationException("請先寄送目前超級管理員的 Email 驗證碼。");
            var code = otp.Text.Trim();
            if (code.Length != 6 || !code.All(char.IsAsciiDigit))
                throw new InvalidOperationException("Email 驗證碼必須是 6 碼數字。");

            if (MessageBox.Show(
                    this,
                    $"完成後：\n{actor.EmployeeNo} {actor.Name} → 管理員\n{target.EmployeeNo} {target.Name} → 超級管理員\nWorkspace Recovery Email → {MaskEmail(target.Email)}\n\n此操作無法以一般角色變更方式復原，確定移交？",
                    "最後確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            Result = await client.ConfirmAsync(
                actor.EmployeeNo,
                actorPassword,
                target.EmployeeNo,
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
                MessageBox.Show(this, FriendlyMessage(error), "超管移交失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        confirm.Enabled = !busy && challenge is not null;
        otp.Enabled = !busy;
        cancel.Enabled = !busy;
    }

    private static string FriendlyMessage(Exception error)
    {
        if (error is not CloudApiException api) return error.Message;
        return api.Code switch
        {
            "SUPER_ADMIN_REQUIRED" => "目前超級管理員帳密驗證失敗，或此帳號已不再是 Workspace 超級管理員。",
            "TRANSFER_TARGET_NOT_READY" => "接任者必須仍是啟用中的管理員，而且 Email 與登入憑證都已完成驗證。",
            "SUPER_ADMIN_RECOVERY_EMAIL_MISMATCH" => "Workspace Recovery Email 與目前超級管理員的已驗證 Email 不一致，已停止移交。",
            "EMAIL_PROVIDER_NOT_CONFIGURED" => "Cloud Email 寄送服務尚未完成設定，現在不能移交超級管理員。",
            "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
            "OTP_INVALID" => "Email 驗證碼錯誤。",
            "OTP_EXPIRED" => "Email 驗證碼已過期，請重新寄送。",
            "OTP_ATTEMPTS_EXHAUSTED" => "Email 驗證碼錯誤次數已達上限，請重新開始移交流程。",
            "OTP_RESEND_COOLDOWN" => "驗證碼剛寄出，請稍後再重新寄送。",
            _ => $"Cloud API 錯誤：{api.Code}\n{api.Message}",
        };
    }

    private static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return "已驗證 Email";
        var local = email[..at];
        var domain = email[(at + 1)..];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***@{domain}";
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "移交超級管理員權限" || ShowIcon || AcceptButton is not null || CancelButton != cancel
            || send.Text != "寄送驗證碼" || confirm.Text != "確認移交" || otp.MaxLength != 6)
            throw new InvalidOperationException("超級管理員移交視窗配置不正確");
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
