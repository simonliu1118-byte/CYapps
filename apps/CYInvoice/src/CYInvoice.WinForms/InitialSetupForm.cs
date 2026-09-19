using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class InitialSetupForm : Form
{
    private const int WindowWidth = 470;
    private const int HeaderHeight = 56;
    private const int FieldRowHeight = 38;
    private const int ActionRowHeight = 48;
    private const int HorizontalPadding = 18;
    private const int VerticalPadding = 10;

    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly bool legacyPasswordRequired;
    private readonly bool moPasswordRequired;
    private readonly TextBox legacyPassword = PasswordBox();
    private readonly TextBox moPassword = PasswordBox();
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox employeeName = UiControls.TextBox(80);
    private readonly TextBox email = UiControls.TextBox(160);
    private readonly TextBox employeePassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = UiControls.StandardButton("建立超級管理員");
    private readonly Button cancel = UiControls.StandardButton("取消並關閉");

    public InitialSetupForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        legacyPasswordRequired = settings.AdminPasswordSet;
        moPasswordRequired = MoPasswordNeedsSetup();
        Text = legacyPasswordRequired ? "CYInvoice V2.5 帳戶遷移" : "CYInvoice 首次設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        BuildLayout();
        Shown += (_, _) => FirstField().Focus();
    }

    private int FieldCount => 5 + (legacyPasswordRequired ? 1 : 0) + (moPasswordRequired ? 1 : 0);

    private int CalculateHeight() =>
        VerticalPadding * 2 + HeaderHeight + FieldCount * FieldRowHeight + ActionRowHeight;

    private bool MoPasswordNeedsSetup()
    {
        if (settings.MoPasswordEncrypted.Length == 0) return true;
        try
        {
            return string.IsNullOrWhiteSpace(repository.Settings.MoPassword(settings));
        }
        catch (Exception)
        {
            return true;
        }
    }

    private void BuildLayout()
    {
        foreach (var field in InputFields()) ConfigureInputField(field);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(HorizontalPadding, VerticalPadding, HorizontalPadding, VerticalPadding),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldCount * FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(new Label
        {
            Text = legacyPasswordRequired
                ? "請先驗證舊版 CYInvoice 管理密碼，再建立第一位超級管理員。完成後舊管理密碼即退出使用。"
                : "請建立第一位超級管理員。復原碼將在完成後只顯示一次，請妥善保存。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = Padding.Empty,
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = FieldCount,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < FieldCount; index++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));

        var row = 0;
        if (legacyPasswordRequired)
        {
            fields.Controls.Add(FieldLabel("舊管理密碼"), 0, row);
            fields.Controls.Add(legacyPassword, 1, row++);
        }
        if (moPasswordRequired)
        {
            fields.Controls.Add(FieldLabel("MO店+ Excel 密碼"), 0, row);
            fields.Controls.Add(moPassword, 1, row++);
        }
        fields.Controls.Add(FieldLabel("員工編號"), 0, row);
        fields.Controls.Add(employeeNo, 1, row++);
        fields.Controls.Add(FieldLabel("姓名"), 0, row);
        fields.Controls.Add(employeeName, 1, row++);
        fields.Controls.Add(FieldLabel("Email"), 0, row);
        fields.Controls.Add(email, 1, row++);
        fields.Controls.Add(FieldLabel("員工密碼"), 0, row);
        fields.Controls.Add(employeePassword, 1, row++);
        fields.Controls.Add(FieldLabel("再次輸入密碼"), 0, row);
        fields.Controls.Add(confirmPassword, 1, row);
        ConfigureEnterOrder();
        root.Controls.Add(fields, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        save.Click += SaveClicked;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private IEnumerable<TextBox> InputFields()
    {
        if (legacyPasswordRequired) yield return legacyPassword;
        if (moPasswordRequired) yield return moPassword;
        yield return employeeNo;
        yield return employeeName;
        yield return email;
        yield return employeePassword;
        yield return confirmPassword;
    }

    private static void ConfigureInputField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
        field.TextAlign = HorizontalAlignment.Left;
    }

    private void ConfigureEnterOrder()
    {
        var ordered = InputFields().ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].TabIndex = index;
            if (index + 1 < ordered.Count)
            {
                var next = ordered[index + 1];
                ordered[index].KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, next);
            }
            else
            {
                ordered[index].KeyDown += (_, eventArgs) => CompleteOnEnter(eventArgs);
            }
        }
    }

    private Control FirstField() => legacyPasswordRequired
        ? legacyPassword
        : moPasswordRequired
            ? moPassword
            : employeeNo;

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (repository.Employees.HasEmployees())
        {
            MessageBox.Show(this, "已建立員工帳戶，不能再次執行首次設定。", "無法建立帳戶",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (legacyPasswordRequired && !SettingsStore.CheckAdminPassword(settings, legacyPassword.Text))
        {
            ValidationError("舊管理密碼不正確", legacyPassword);
            return;
        }
        if (moPasswordRequired && moPassword.Text.Length == 0)
        {
            ValidationError("請輸入 MO店+ Excel 保護密碼", moPassword);
            return;
        }
        var no = employeeNo.Text.Trim();
        if (no.Length != 4 || !no.All(char.IsDigit))
        {
            ValidationError("員工編號必須為 4 碼數字", employeeNo);
            return;
        }
        if (employeeName.Text.Trim().Length == 0)
        {
            ValidationError("請輸入員工姓名", employeeName);
            return;
        }
        if (email.Text.Trim().Length == 0)
        {
            ValidationError("請輸入 Email", email);
            return;
        }
        if (employeePassword.Text.Length == 0)
        {
            ValidationError("請輸入員工密碼", employeePassword);
            return;
        }
        if (confirmPassword.Text.Length == 0 || employeePassword.Text != confirmPassword.Text)
        {
            ValidationError("兩次輸入的員工密碼不一致", confirmPassword);
            return;
        }

        try
        {
            if (moPasswordRequired)
            {
                repository.Settings.SetMoPassword(settings, moPassword.Text);
                repository.Settings.Save(settings);
            }

            var setup = repository.Employees.CreateFirstSuperAdmin(
                no,
                employeeName.Text,
                email.Text,
                employeePassword.Text);

            if (legacyPasswordRequired)
            {
                repository.Settings.RetireLegacyAdminPassword(settings);
                repository.Settings.Save(settings);
            }

            using var recovery = new RecoveryCodeForm(setup.RecoveryCode);
            recovery.ShowDialog(this);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法完成帳戶設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        save.PerformClick();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "無法完成帳戶設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        var fields = InputFields().ToArray();
        if (Icon is null || employeeNo.MaxLength != 4 || !employeePassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar ||
            (legacyPasswordRequired && !legacyPassword.UseSystemPasswordChar) ||
            (moPasswordRequired && !moPassword.UseSystemPasswordChar) ||
            fields.Any(field => field.TextAlign != HorizontalAlignment.Left) ||
            ClientSize.Width != WindowWidth || ClientSize.Height != CalculateHeight() ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(save, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("V2.5 首次帳戶設定視窗配置不正確");
    }

    private static TextBox PasswordBox()
    {
        var field = UiControls.TextBox(200);
        field.UseSystemPasswordChar = true;
        return field;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 5, 0, 0);
    }
}
