using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SelfAccountManagementForm : Form
{
    private readonly LocalRepository repository;
    private EmployeeAccount account;
    private readonly string authenticatedPassword;
    private readonly HttpClient httpClient;
    private readonly Label currentEmail = new() { Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly TextBox email = UiControls.TextBox(254);
    private readonly Button changePassword = UiControls.StandardButton("更改密碼");
    private readonly Button changeEmail = UiControls.StandardButton("更改 Email");
    private readonly Button close = UiControls.StandardButton("關閉");

    public SelfAccountManagementForm(LocalRepository repository, EmployeeAccount account,
        string authenticatedPassword, HttpClient httpClient)
    {
        this.repository = repository;
        this.account = account;
        this.authenticatedPassword = authenticatedPassword;
        this.httpClient = httpClient;
        Text = "帳號管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(485, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(14, 12, 14, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(new Label { Text = $"{account.EmployeeNo}  {account.Name}", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10F, FontStyle.Bold) }, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        fields.Controls.Add(new Label { Text = "目前 Email", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        currentEmail.Text = account.Email;
        fields.Controls.Add(currentEmail, 1, 0);
        fields.Controls.Add(new Label { Text = "新 Email", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        email.Text = account.Email;
        email.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        fields.Controls.Add(email, 1, 1);
        root.Controls.Add(fields, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        close.DialogResult = DialogResult.Cancel;
        changePassword.Click += (_, _) => ChangePassword();
        changeEmail.Click += async (_, _) => await ChangeEmailAsync();
        buttons.Controls.Add(close);
        buttons.Controls.Add(changeEmail);
        buttons.Controls.Add(changePassword);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        if (!repository.UsesCloudEmployeeAuthority())
        {
            email.Enabled = false;
            changeEmail.Enabled = false;
            var tip = new ToolTip();
            tip.SetToolTip(changeEmail, "Email 驗證需要雲端版；單機版請由管理員處理帳號資料。");
        }
        CancelButton = close;
    }

    private void ChangePassword()
    {
        using var form = new EmployeeChangePasswordForm(repository, account, httpClient);
        if (form.ShowDialog(this) == DialogResult.OK)
            MessageBox.Show(this, "密碼已變更。", "帳號管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ChangeEmailAsync()
    {
        if (!repository.UsesCloudEmployeeAuthority()) return;
        var proposed = email.Text.Trim();
        if (proposed.Length == 0 || !proposed.Contains('@'))
        {
            MessageBox.Show(this, "請輸入有效的新 Email。", "無法更改 Email",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.Equals(proposed, account.Email, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "新 Email 與目前相同。", "帳號管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        changeEmail.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var settings = repository.Settings.LoadOrCreate();
            var token = repository.Settings.CloudDeviceToken(settings);
            var baseUri = new Uri(settings.CloudBaseUrl, UriKind.Absolute);
            var client = new CloudEmployeeAccountClient(httpClient, baseUri, token);
            var proposal = new CloudEmployeeUpdateProposal(account.EmployeeNo, account.Name, proposed, account.Role);
            var started = await client.StartUpdateAsync(account.EmployeeNo, authenticatedPassword, proposal);
            if (!started.VerificationRequired || started.Challenge is null)
                throw new InvalidDataException("雲端未要求新 Email 驗證，資料未變更。");
            using var verify = new CloudEmployeeUpdateVerificationForm(client, account,
                authenticatedPassword, proposal, started.Challenge);
            if (verify.ShowDialog(this) != DialogResult.OK || verify.UpdatedEmployee is null) return;
            var authority = new CloudEmployeeAuthorityClient(httpClient, baseUri, token);
            var snapshot = await authority.GetSnapshotAsync();
            if (snapshot.WorkspaceId != settings.CloudWorkspaceId)
                throw new InvalidDataException("雲端帳號快取回傳的 Workspace 不一致。");
            repository.CloudEmployees.ReplaceSnapshot(snapshot.WorkspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
            account = account with { Email = verify.UpdatedEmployee.Email };
            email.Text = account.Email;
            currentEmail.Text = account.Email;
            MessageBox.Show(this, "新 Email 已驗證並更新。", "帳號管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception problem)
        {
            var message = problem is CloudApiException api ? api.Code switch
            {
                "EMPLOYEE_AUTHENTICATION_FAILED" => "帳號驗證已失效，請關閉視窗後重新登入。",
                "EMPLOYEE_EMAIL_EXISTS" => "這個 Email 已由其他員工使用。",
                "EMAIL_DELIVERY_FAILED" => "驗證信目前無法寄出，請稍後再試。",
                "EMAIL_PROVIDER_NOT_CONFIGURED" or "OTP_NOT_CONFIGURED" => "雲端 Email 寄送服務尚未設定。",
                _ => $"雲端 Email 更新失敗：{api.Code}",
            } : problem.Message;
            MessageBox.Show(this, message, "無法更改 Email", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) { changeEmail.Enabled = true; UseWaitCursor = false; }
        }
    }
}
