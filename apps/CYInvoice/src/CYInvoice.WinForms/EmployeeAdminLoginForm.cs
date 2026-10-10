using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class EmployeeAdminLoginForm : Form
{
    private const string WindowTitle = "權限驗證";
    private const int WindowWidth = 280;
    private const int WindowHeight = 126;
    private const int FieldRowHeight = 34;
    private const int ActionRowHeight = 40;
    private const int CompactButtonWidth = 70;
    private const int CompactButtonHeight = 28;
    private readonly IIdentityProvider identityProvider;
    private readonly bool administratorRequired;
    private readonly bool collectCredentialsOnly;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly Button login = CompactButton("確定");
    private readonly Button cancel = CompactButton("取消");

    public EmployeeAdminLoginForm(EmployeeStore employees, string title = WindowTitle)
        : this(new LocalIdentityProvider(employees), title, true)
    {
        ArgumentNullException.ThrowIfNull(employees);
    }

    public EmployeeAdminLoginForm(LocalRepository repository, string title = WindowTitle)
        : this(repository.IdentityProvider, title, true)
    {
        ArgumentNullException.ThrowIfNull(repository);
    }

    public EmployeeAdminLoginForm(LocalRepository repository, string title, bool administratorRequired = true, bool collectCredentialsOnly = false)
        : this(repository.IdentityProvider, title, administratorRequired, collectCredentialsOnly)
    {
        ArgumentNullException.ThrowIfNull(repository);
    }

    internal EmployeeAdminLoginForm(IIdentityProvider identityProvider, string title, bool administratorRequired, bool collectCredentialsOnly = false)
    {
        this.identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        this.administratorRequired = administratorRequired;
        this.collectCredentialsOnly = collectCredentialsOnly;
        Text = string.IsNullOrWhiteSpace(title) ? WindowTitle : title.Trim();
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
    }

    public EmployeeAccount? AuthenticatedEmployee { get; private set; }
    public AppPrincipal? AuthenticatedPrincipal { get; private set; }
    public string AuthenticatedPassword => collectCredentialsOnly || AuthenticatedPrincipal is not null ? password.Text : string.Empty;
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
        login.Click += LoginClicked;
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

    private async void LoginClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (collectCredentialsOnly)
            {
                // The final core operation is the authority owner. Collecting
                // credentials grants no principal or permission in this dialog.
                if (EnteredEmployeeNo.Length != 4 || !EnteredEmployeeNo.All(char.IsAsciiDigit) || password.Text.Length == 0)
                {
                    ValidationError("請輸入四位員工編號與密碼。", password);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            var principal = await identityProvider.AuthenticateAsync(
                new IdentityAuthenticationRequest(employeeNo.Text, password.Text));
            if (principal is null)
            {
                ValidationError("員工編號或密碼錯誤", password);
                return;
            }
            if (administratorRequired && !AppRoles.CanManageAccounts(principal.Role))
            {
                MessageBox.Show(this, "權限不足，僅管理員可執行此操作。", "權限不足",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AuthenticatedPrincipal = principal;
            AuthenticatedEmployee = principal.ToEmployeeAccount();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            ValidationError(identityProvider.Kind == IdentityProviderKind.Local && error is InvalidOperationException
                ? "員工編號或密碼錯誤" : error.Message, employeeNo);
        }
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
        MessageBox.Show(this, message, "驗證失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal static void VerifyCredentialsCollectionSmoke(Form owner)
    {
        var provider = new CollectionProbeProvider();
        using var form = new EmployeeAdminLoginForm(provider, "操作驗證", true, collectCredentialsOnly: true);
        form.Shown += (_, _) =>
        {
            form.employeeNo.Text = "0002";
            form.password.Text = "SyntheticPass1";
            form.login.PerformClick();
        };
        if (form.ShowDialog(owner) != DialogResult.OK || provider.Calls != 0 || form.AuthenticatedPrincipal is not null
            || form.AuthenticatedEmployee is not null || form.EnteredEmployeeNo != "0002" || form.AuthenticatedPassword != "SyntheticPass1")
            throw new InvalidOperationException("操作帳密輸入不得額外呼叫 Provider 或產生授權。");
    }

    private sealed class CollectionProbeProvider : IIdentityProvider
    {
        public int Calls { get; private set; }
        public IdentityProviderKind Kind => IdentityProviderKind.CyId;
        public bool OwnsAccountManagement => false;
        public Task<AppPrincipal?> AuthenticateAsync(IdentityAuthenticationRequest request, CancellationToken token = default)
        {
            Calls++;
            return Task.FromResult<AppPrincipal?>(new AppPrincipal("emp_synthetic", "0002", "Synthetic", "",
                AppRole.Admin, true, IdentityProviderKind.CyId, 1));
        }
        public Task<AppPrincipal?> RefreshPrincipalAsync(string employeeNo, CancellationToken token = default) =>
            throw new InvalidOperationException("Credential collection must not refresh a principal.");
    }

    internal void VerifySmokeLayout()
    {
        if (ShowIcon || !password.UseSystemPasswordChar || employeeNo.MaxLength != 4 ||
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
