namespace CYInvoice.WinForms;

internal sealed class AllowanceVoidRequestForm : Form
{
    private const int WindowWidth = 360;
    private const int WindowHeight = 205;
    private readonly TextBox reason = UiControls.TextBox(100);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(128);
    private readonly Button submit = UiControls.StandardButton("提出申請");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public AllowanceVoidRequestForm()
    {
        Text = "折讓作廢申請";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        reason.TextAlign = HorizontalAlignment.Left;
        employeeNo.TextAlign = HorizontalAlignment.Left;
        password.TextAlign = HorizontalAlignment.Left;
        password.UseSystemPasswordChar = true;
        BuildLayout();
        Shown += (_, _) => reason.Focus();
    }

    public string Reason => reason.Text.Trim();
    public string EmployeeNo => employeeNo.Text.Trim();
    public string Password => password.Text;

    private void BuildLayout()
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        AddField(fields, 0, "作廢原因", reason);
        AddField(fields, 1, "員工編號", employeeNo);
        AddField(fields, 2, "員工密碼", password);

        WireEnter(reason, employeeNo);
        WireEnter(employeeNo, password);
        password.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            submit.PerformClick();
        };

        submit.Click += (_, _) => Submit();
        cancel.DialogResult = DialogResult.Cancel;
        submit.Width = 96;
        cancel.Width = 96;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(submit);
        buttons.SizeChanged += (_, _) =>
        {
            var width = buttons.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
            buttons.Padding = new Padding(Math.Max(0, (buttons.ClientSize.Width - width) / 2), 4, 0, 0);
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 10, 14, 8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        root.Controls.Add(new Label
        {
            Text = "請輸入折讓作廢申請資料",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Margin = Padding.Empty,
        }, 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void Submit()
    {
        if (Reason.Length == 0)
        {
            ValidationError("請輸入折讓作廢原因。", reason);
            return;
        }
        if (EmployeeNo.Length == 0)
        {
            ValidationError("請輸入員工編號。", employeeNo);
            return;
        }
        if (Password.Length == 0)
        {
            ValidationError("請輸入員工密碼。", password);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddField(TableLayoutPanel layout, int row, string label, Control field)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        }, 0, row);
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(field, 1, row);
    }

    private static void WireEnter(Control source, Control next)
    {
        source.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            next.Focus();
        };
    }

    private void ValidationError(string message, TextBox field)
    {
        MessageBox.Show(this, message, "資料未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            field.Focus();
            field.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "折讓作廢申請" || ShowIcon || ClientSize.Width != WindowWidth || ClientSize.Height != WindowHeight ||
            AcceptButton is not null || CancelButton != cancel || !password.UseSystemPasswordChar)
            throw new InvalidOperationException("折讓作廢申請視窗基本配置不正確");
    }
}
