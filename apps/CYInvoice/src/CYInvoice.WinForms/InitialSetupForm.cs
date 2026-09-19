using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class InitialSetupForm : Form
{
    private const int WindowWidth = 330;
    private const int HeaderHeight = 38;
    private const int FieldRowHeight = 34;
    private const int ActionRowHeight = 42;
    private const int HorizontalPadding = 12;
    private const int VerticalPadding = 6;
    private const int FieldCount = 5;
    private const int CompactButtonWidth = 118;
    private const int CompactButtonHeight = 30;

    private readonly LocalRepository repository;
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox employeeName = UiControls.TextBox(80);
    private readonly TextBox email = UiControls.TextBox(160);
    private readonly TextBox employeePassword = PasswordBox();
    private readonly TextBox confirmPassword = PasswordBox();
    private readonly Button save = CompactButton("建立超級管理員");
    private readonly Button cancel = CompactButton("取消並關閉");

    public InitialSetupForm(LocalRepository repository)
    {
        this.repository = repository;
        Text = "首次設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        Shown += (_, _) => employeeNo.Focus();
    }

    private static int CalculateHeight() =>
        VerticalPadding * 2 + HeaderHeight + FieldCount * FieldRowHeight + ActionRowHeight;

    private void BuildLayout()
    {
        foreach (var field in InputFields()) ConfigureInputField(field);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(HorizontalPadding, VerticalPadding, HorizontalPadding, VerticalPadding),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldCount * FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(new Label
        {
            Text = "首次開啟程式需設定超級管理員，超級管理員無法變更。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = FieldCount,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < FieldCount; index++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));

        var row = 0;
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

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (repository.Employees.HasEmployees())
        {
            MessageBox.Show(this, "已建立員工帳號，不能再次執行首次設定。", "無法建立帳號",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        if (employeePassword.Text.Length < 8 || !employeePassword.Text.All(char.IsAsciiLetterOrDigit))
        {
            ValidationError("員工密碼至少 8 碼，且只能使用英文字母或數字", employeePassword);
            return;
        }
        if (confirmPassword.Text.Length == 0 || employeePassword.Text != confirmPassword.Text)
        {
            ValidationError("兩次輸入的員工密碼不一致", confirmPassword);
            return;
        }

        try
        {
            var setup = repository.Employees.CreateFirstSuperAdmin(
                no,
                employeeName.Text,
                email.Text,
                employeePassword.Text);
            using var recovery = new RecoveryCodeForm(setup.RecoveryCode);
            recovery.ShowDialog(this);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法完成帳號設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        MessageBox.Show(this, message, "無法完成帳號設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        var fields = InputFields().ToArray();
        if (Text != "首次設定" || ShowIcon || employeeNo.MaxLength != 4 || !employeePassword.UseSystemPasswordChar || !confirmPassword.UseSystemPasswordChar ||
            fields.Any(field => field.TextAlign != HorizontalAlignment.Left) ||
            ClientSize.Width != WindowWidth || ClientSize.Height != CalculateHeight() ||
            AcceptButton is not null || CancelButton != cancel ||
            !UiControls.HasLogicalSize(save, CompactButtonWidth, CompactButtonHeight))
            throw new InvalidOperationException("V2.6.1 首次設定視窗配置不正確");
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
        Margin = new Padding(0, 0, 6, 0),
    };

    private static Button CompactButton(string text) => new NoFocusCueButton
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
