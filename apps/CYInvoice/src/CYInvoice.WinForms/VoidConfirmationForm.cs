using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class VoidConfirmationForm : Form
{
    private const string ResponsibilityText =
        "本人已核對本次作廢之發票號碼、交易事實及作廢原因，並了解錯誤或不當作廢發票可能涉及稅務法令及公司內部責任；如因本人故意或過失造成錯誤作廢，本人願依相關法令及公司規定承擔應負之責任。\n\n按下「確認作廢」即表示本人已閱讀並確認上述事項。";

    private readonly bool paperInvoice;
    private readonly TextBox invoiceNumber = UiControls.TextBox(10);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly ComboBox receiptState = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button confirm = UiControls.StandardButton("確認作廢");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public VoidConfirmationForm(bool paperInvoice)
    {
        this.paperInvoice = paperInvoice;
        Text = "發票作廢確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, paperInvoice ? 410 : 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        BuildLayout();
        Shown += (_, _) => invoiceNumber.Focus();
    }

    public string EnteredInvoiceNumber => invoiceNumber.Text.Trim();
    public string EmployeeNo => employeeNo.Text.Trim();
    public string Password => password.Text;
    public string PaperReceiptState => paperInvoice ? SelectedReceiptState() : string.Empty;

    private void BuildLayout()
    {
        password.UseSystemPasswordChar = true;
        employeeNo.TextAlign = HorizontalAlignment.Center;
        invoiceNumber.CharacterCasing = CharacterCasing.Upper;
        if (paperInvoice)
        {
            receiptState.Items.Add(new ReceiptChoice("未列印／未交付", PaperInvoiceReceiptStates.NotPrintedOrNotDelivered));
            receiptState.Items.Add(new ReceiptChoice("已收回", PaperInvoiceReceiptStates.Collected));
            receiptState.Items.Add(new ReceiptChoice("尚未收回", PaperInvoiceReceiptStates.Uncollected));
            receiptState.DisplayMember = nameof(ReceiptChoice.Text);
        }

        var fieldRows = paperInvoice ? 4 : 3;
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = fieldRows,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < fieldRows; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        AddField(fields, "發票號碼", invoiceNumber, 0);
        AddField(fields, "員工編號", employeeNo, 1);
        AddField(fields, "員工密碼", password, 2);
        if (paperInvoice) AddField(fields, "證明聯狀態", receiptState, 3);

        invoiceNumber.TabIndex = 0;
        employeeNo.TabIndex = 1;
        password.TabIndex = 2;
        if (paperInvoice) receiptState.TabIndex = 3;
        invoiceNumber.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, employeeNo);
        employeeNo.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, password);
        password.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, paperInvoice ? receiptState : confirm);

        var responsibility = new Label
        {
            Text = ResponsibilityText,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.FromArgb(65, 65, 65),
            Padding = new Padding(4, 10, 4, 4),
        };

        confirm.Click += (_, _) => Submit();
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.Controls.Add(confirm);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRows * 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(responsibility, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void Submit()
    {
        if (invoiceNumber.Text.Trim().Length == 0)
        {
            ValidationError("請輸入發票號碼。", invoiceNumber);
            return;
        }
        if (employeeNo.Text.Trim().Length != 4 || !employeeNo.Text.Trim().All(char.IsAsciiDigit))
        {
            ValidationError("員工編號必須為 4 碼數字。", employeeNo);
            return;
        }
        if (password.Text.Length == 0)
        {
            ValidationError("請輸入員工密碼。", password);
            return;
        }
        if (paperInvoice && receiptState.SelectedItem is null)
        {
            MessageBox.Show(this, "請選擇證明聯狀態。", "資料未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            receiptState.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private string SelectedReceiptState() =>
        receiptState.SelectedItem is ReceiptChoice choice ? choice.Value : string.Empty;

    private static void AddField(TableLayoutPanel panel, string label, Control field, int row)
    {
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
        }, 0, row);
        field.Dock = DockStyle.Fill;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 8, 0, 8);
        panel.Controls.Add(field, 1, row);
    }

    private static void AdvanceOnEnter(KeyEventArgs eventArgs, Control next)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        next.Focus();
    }

    private void ValidationError(string message, TextBox target)
    {
        MessageBox.Show(this, message, "資料未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            target.Focus();
            target.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "發票作廢確認" || invoiceNumber.Text.Length != 0 || password.Text.Length != 0 ||
            AcceptButton is not null || CancelButton != cancel || !password.UseSystemPasswordChar)
            throw new InvalidOperationException("發票作廢確認視窗基本配置不正確");
        if (paperInvoice && receiptState.Items.Count != 3)
            throw new InvalidOperationException("紙本發票證明聯狀態選項不完整");
        if (!paperInvoice && receiptState.Parent is not null)
            throw new InvalidOperationException("非紙本發票不應顯示證明聯狀態");
    }

    private sealed record ReceiptChoice(string Text, string Value)
    {
        public override string ToString() => Text;
    }
}
