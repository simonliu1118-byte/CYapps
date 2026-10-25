using CYInvoice.Core.Storage;
using CYInvoice.Core.Cloud;

namespace CYInvoice.WinForms;

internal sealed class EmployeeAdminLoginForm : Form
{
    internal const string WindowTitle = "密碼驗證";
    private const int WindowWidth = 280;
    private const int WindowHeight = 126;
    private const int FieldRowHeight = 34;
    private const int ActionRowHeight = 40;
    private const int CompactButtonWidth = 70;
    private const int CompactButtonHeight = 28;
    private readonly IIdentityProvider identityProvider;
    private readonly AppRole? requiredRole;
    private readonly bool requireOnline;
    private readonly Action<string>? errorReporter;
    private readonly CancellationTokenSource lifetime = new();
    private bool authenticating;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly Button login = CompactButton("確定");
    private readonly Button cancel = CompactButton("取消");

    public EmployeeAdminLoginForm(EmployeeStore employees)
        : this(new LocalIdentityProvider(employees)) { }

    public EmployeeAdminLoginForm(LocalRepository repository, AppRole? requiredRole = AppRole.Admin, bool requireOnline = false)
        : this(repository.IdentityProvider, requiredRole, requireOnline) { }

    internal EmployeeAdminLoginForm(IIdentityProvider identityProvider, AppRole? requiredRole = AppRole.Admin,
        bool requireOnline = false, Action<string>? errorReporter = null)
    {
        this.identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        this.requiredRole = requiredRole;
        this.requireOnline = requireOnline;
        this.errorReporter = errorReporter;
        Text = WindowTitle;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => employeeNo.Focus();
        FormClosing += (_, _) => lifetime.Cancel();
    }

    public EmployeeAccount? AuthenticatedEmployee { get; private set; }
    public AppPrincipal? AuthenticatedPrincipal { get; private set; }
    public string AuthenticatedPassword => AuthenticatedPrincipal is not null ? password.Text : string.Empty;
    public string EnteredEmployeeNo => employeeNo.Text.Trim();

    private void BuildLayout()
    {
        password.UseSystemPasswordChar = true;
        ConfigureInputField(employeeNo);
        ConfigureInputField(password);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 6, 14, 6),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        fields.Controls.Add(FieldLabel("員工編號"), 0, 0);
        fields.Controls.Add(employeeNo, 1, 0);
        fields.Controls.Add(FieldLabel("密碼"), 0, 1);
        fields.Controls.Add(password, 1, 1);
        employeeNo.TabIndex = 0;
        password.TabIndex = 1;
        employeeNo.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, password);
        password.KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
        root.Controls.Add(fields, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        login.Click += async (_, _) => await AuthenticateAsync();
        cancel.DialogResult = DialogResult.Cancel;
        login.TabIndex = 2;
        cancel.TabIndex = 3;
        buttons.Controls.Add(login);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 1);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private static void ConfigureInputField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
        field.TextAlign = HorizontalAlignment.Left;
    }

    private async Task AuthenticateAsync()
    {
        if (authenticating || lifetime.IsCancellationRequested) return;
        authenticating = true;
        login.Enabled = false;
        employeeNo.Enabled = password.Enabled = false;
        try
        {
            var principal = await identityProvider.AuthenticateAsync(
                new IdentityAuthenticationRequest(employeeNo.Text, password.Text) { RequireOnline = requireOnline }, lifetime.Token);
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            if (principal is null)
            {
                ValidationError("帳號或密碼錯誤", password);
                return;
            }
            var allowed = principal.Enabled && (requiredRole switch {
                AppRole.SuperAdmin => principal.Role == AppRole.SuperAdmin,
                AppRole.Admin => AppRoles.CanManageAccounts(principal.Role),
                _ => true,
            });
            if (!allowed)
            {
                ValidationError("權限不足", password);
                return;
            }
            AuthenticatedPrincipal = principal;
            AuthenticatedEmployee = principal.ToEmployeeAccount();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsDisposed) ValidationError(AuthenticationErrorMessage(error), password);
        }
        finally
        {
            authenticating = false;
            if (!IsDisposed)
            {
                login.Enabled = employeeNo.Enabled = password.Enabled = true;
                if (Visible && DialogResult != DialogResult.OK) { password.Focus(); password.SelectAll(); }
            }
        }
    }

    internal static string AuthenticationErrorMessage(Exception error)
    {
        var code = error is CyIdAuthenticationException identity ? identity.Code :
            error is CloudApiException cloud ? cloud.Code : string.Empty;
        return code switch {
            "LOGIN_FAILED" or "AUTH_INVALID" or "SUPER_ADMIN_AUTH_FAILED" => "帳號或密碼錯誤",
            "ACCESS_DENIED" or "MANAGER_AUTH_REQUIRED" or "SUPER_ADMIN_REQUIRED" or "FORBIDDEN" => "權限不足",
            "DEVICE_INVALID" or "UNAUTHORIZED" => "目前裝置授權已失效，請確認裝置與連線。",
            "IDENTITY_UNAVAILABLE" => "目前無法連線驗證服務，請稍後再試。",
            _ when error is HttpRequestException => "目前無法連線驗證服務，請稍後再試。",
            _ when error is OperationCanceledException => "驗證連線逾時，請稍後再試。",
            _ => error.Message,
        };
    }

    private static void AdvanceOnEnter(KeyEventArgs eventArgs, Control next)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        next.Focus();
    }

    private void CompleteOnEnter(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        login.PerformClick();
    }

    private void ValidationError(string message, TextBox target)
    {
        if (errorReporter is not null) errorReporter(message);
        else MessageBox.Show(this, message, WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (IsDisposed) return;
        target.Focus();
        target.SelectAll();
    }

    internal static void VerifyAuthenticationSmoke(Form owner)
    {
        var provider = new AuthenticationProbeProvider();
        var errors = new List<string>();
        using var form = new EmployeeAdminLoginForm(provider, AppRole.SuperAdmin, requireOnline: true, errorReporter: errors.Add);
        form.Show(owner);
        form.employeeNo.Text = "0002";
        form.password.Text = "SyntheticPass1";
        void Attempt()
        {
            // The synthetic provider completes synchronously; exercise the real
            // authentication/error path without native MessageBox automation.
            var attempt = form.AuthenticateAsync();
            if (!attempt.IsCompleted) throw new InvalidOperationException("Synthetic authentication must complete synchronously.");
            attempt.GetAwaiter().GetResult();
        }
        void AssertFailure(string expected)
        {
            if (!form.Visible || form.DialogResult == DialogResult.OK || form.AuthenticatedPrincipal is not null ||
                errors.LastOrDefault() != expected || form.Text != WindowTitle || !form.login.Enabled)
                throw new InvalidOperationException("驗證失敗必須提示對應原因並維持密碼驗證視窗。");
        }
        Attempt(); AssertFailure("帳號或密碼錯誤");
        provider.Role = AppRole.Admin;
        Attempt(); AssertFailure("權限不足");
        provider.Error = new CyIdAuthenticationException("ACCESS_DENIED");
        Attempt(); AssertFailure("權限不足");
        provider.Error = new HttpRequestException("Synthetic offline");
        Attempt(); AssertFailure("目前無法連線驗證服務，請稍後再試。");
        provider.Error = null; provider.Role = AppRole.SuperAdmin;
        Attempt();
        if (form.AuthenticatedPrincipal?.Role != AppRole.SuperAdmin || form.DialogResult != DialogResult.OK ||
            provider.Calls != 5 || !provider.RequiredOnline || form.AuthenticatedPassword.Length != 0)
            throw new InvalidOperationException("只有當次帳密與指定角色均驗證成功才能關閉驗證窗。");
    }

    private sealed class AuthenticationProbeProvider : IIdentityProvider
    {
        public AppRole? Role { get; set; }
        public Exception? Error { get; set; }
        public int Calls { get; private set; }
        public bool RequiredOnline { get; private set; }
        public IdentityProviderKind Kind => IdentityProviderKind.CyId;
        public bool OwnsAccountManagement => false;
        public Task<AppPrincipal?> AuthenticateAsync(IdentityAuthenticationRequest request, CancellationToken token = default)
        {
            Calls++; RequiredOnline = request.RequireOnline;
            if (Error is not null) throw Error;
            return Task.FromResult(Role is null ? null : new AppPrincipal("emp_synthetic", "0002", "Synthetic", "",
                Role.Value, true, IdentityProviderKind.CyId, 1));
        }
        public Task<AppPrincipal?> RefreshPrincipalAsync(string employeeNo, CancellationToken token = default) =>
            throw new InvalidOperationException("Authentication must verify credentials.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            lifetime.Cancel();
            password.Clear();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    internal void VerifySmokeLayout()
    {
        if (Text != WindowTitle || ShowIcon || !password.UseSystemPasswordChar || employeeNo.MaxLength != 4 ||
            employeeNo.TextAlign != HorizontalAlignment.Left || password.TextAlign != HorizontalAlignment.Left ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(login, CompactButtonWidth, CompactButtonHeight) ||
            !UiControls.HasLogicalSize(cancel, CompactButtonWidth, CompactButtonHeight))
            throw new InvalidOperationException("權限驗證視窗配置不正確");
        if (ClientSize.Width != WindowWidth || ClientSize.Height != WindowHeight)
            throw new InvalidOperationException("權限驗證視窗未維持精簡尺寸");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static Button CompactButton(string text) => new RoundedButton
    {
        Text = text,
        Width = CompactButtonWidth,
        Height = CompactButtonHeight,
        Margin = new Padding(5, 2, 5, 2),
        AutoSize = false,
        UseVisualStyleBackColor = true,
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 5, 0, 0);
    }
}
