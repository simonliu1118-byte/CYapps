using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class AllowanceRequestForm : Form
{
    private const int WindowWidth = 390;
    private const int WindowHeight = 250;
    private readonly TextBox reason = UiControls.TextBox(100);
    private readonly TextBox amount = UiControls.TextBox(12);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(128);
    private readonly Button submit = UiControls.StandardButton("提出申請");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public AllowanceRequestForm()
    {
        Text = "折讓申請";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        reason.TextAlign = HorizontalAlignment.Left;
        amount.TextAlign = HorizontalAlignment.Left;
        employeeNo.TextAlign = HorizontalAlignment.Left;
        password.TextAlign = HorizontalAlignment.Left;
        password.UseSystemPasswordChar = true;

        BuildLayout();
        Shown += (_, _) => reason.Focus();
    }

    public string Reason => reason.Text.Trim();
    public long TaxInclusiveAmount { get; private set; }
    public string EmployeeNo => employeeNo.Text.Trim();
    public string Password => password.Text;

    private void BuildLayout()
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        AddField(fields, 0, "折讓原因", reason);
        AddField(fields, 1, "含稅折讓總額", amount);
        AddField(fields, 2, "員工編號", employeeNo);
        AddField(fields, 3, "員工密碼", password);

        WireEnter(reason, amount);
        WireEnter(amount, employeeNo);
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
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(submit);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 12, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.Controls.Add(new Label
        {
            Text = "請輸入本次人工折讓申請資料",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
            ForeColor = Color.DimGray,
        }, 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
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

    private void Submit()
    {
        if (Reason.Length == 0)
        {
            ValidationError("請輸入折讓原因。", reason);
            return;
        }

        var amountText = amount.Text.Trim().Replace(",", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(amountText, out var parsed) || parsed <= 0)
        {
            ValidationError("含稅折讓總額必須是大於 0 的整數。", amount);
            return;
        }
        if (EmployeeNo.Length == 0)
        {
            ValidationError("請輸入員工編號。", employeeNo);
            return;
        }
        if (password.Text.Length == 0)
        {
            ValidationError("請輸入員工密碼。", password);
            return;
        }

        TaxInclusiveAmount = parsed;
        DialogResult = DialogResult.OK;
        Close();
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
        if (Text != "折讓申請" || ClientSize.Width != WindowWidth || ClientSize.Height != WindowHeight ||
            AcceptButton is not null || CancelButton != cancel || !password.UseSystemPasswordChar)
            throw new InvalidOperationException("折讓申請視窗基本配置不正確");
        if (reason.TextAlign != HorizontalAlignment.Left || amount.TextAlign != HorizontalAlignment.Left ||
            employeeNo.TextAlign != HorizontalAlignment.Left || password.TextAlign != HorizontalAlignment.Left)
            throw new InvalidOperationException("折讓申請輸入欄位未保持靠左");
    }
}
