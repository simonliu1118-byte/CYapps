using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class VoidConfirmationForm : Form
{
    private const string ResponsibilityText =
        "本人已核對本次作廢之發票號碼、交易事實、作廢原因及紙本電子發票證明聯收回狀況。\n\n本人了解發票作廢應依相關法令及公司作業規範辦理；如因本人故意、操作錯誤或疏失致生相關問題，願依實際責任歸屬、相關法令及公司規定負應負之責任。\n\n按下「確認作廢」即表示本人已閱讀並確認上述事項。";
    private const string UncollectedWarningText = "未收回作廢需由管理員確認後才會送出";
    private const int WindowWidth = 500;
    private const int FieldRowHeight = 38;
    private const int WarningRowHeight = 28;
    private const int ResponsibilityHeight = 120;
    private const int ActionRowHeight = 46;

    private readonly bool paperInvoice;
    private readonly TextBox invoiceNumber = UiControls.TextBox(10);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly ComboBox receiptState = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label uncollectedWarning = new()
    {
        Text = UncollectedWarningText,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(185, 108, 0),
        Visible = false,
        Margin = Padding.Empty,
    };
    private readonly Button confirm = UiControls.StandardButton("確認作廢");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public VoidConfirmationForm(bool paperInvoice)
    {
        this.paperInvoice = paperInvoice;
        Text = "發票作廢確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, CalculateHeight());
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

    private int FieldRows => paperInvoice ? 4 : 3;
    private int CalculateHeight() =>
        20 + FieldRows * FieldRowHeight + (paperInvoice ? WarningRowHeight : 0) + ResponsibilityHeight + ActionRowHeight;

    private void BuildLayout()
    {
        password.UseSystemPasswordChar = true;
        ConfigureInputField(invoiceNumber);
        ConfigureInputField(employeeNo);
        ConfigureInputField(password);
        invoiceNumber.CharacterCasing = CharacterCasing.Upper;
        if (paperInvoice)
        {
            receiptState.Items.Add(new ReceiptChoice("未列印／未交付", PaperInvoiceReceiptStates.NotPrintedOrNotDelivered));
            receiptState.Items.Add(new ReceiptChoice("已收回", PaperInvoiceReceiptStates.Collected));
            receiptState.Items.Add(new ReceiptChoice("尚未收回", PaperInvoiceReceiptStates.Uncollected));
            receiptState.DisplayMember = nameof(ReceiptChoice.Text);
            receiptState.SelectedIndexChanged += (_, _) => UpdateUncollectedWarning();
            receiptState.Dock = DockStyle.None;
            receiptState.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            receiptState.Margin = new Padding(3, 0, 3, 0);
        }

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = FieldRows,
            Margin = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < FieldRows; row++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        AddField(fields, "發票號碼", invoiceNumber, 0);
        AddField(fields, "員工編號", employeeNo, 1);
        AddField(fields, "員工密碼", password, 2);
        if (paperInvoice)
        {
            fields.Controls.Add(FieldLabel("證明聯狀態"), 0, 3);
            fields.Controls.Add(receiptState, 1, 3);
        }

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
            Padding = new Padding(0, 8, 0, 0),
            Margin = Padding.Empty,
        };

        confirm.Click += (_, _) => Submit();
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(confirm);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRows * FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, paperInvoice ? WarningRowHeight : 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsibilityHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(fields, 0, 0);
        if (paperInvoice) root.Controls.Add(uncollectedWarning, 0, 1);
        root.Controls.Add(responsibility, 0, 2);
        root.Controls.Add(buttons, 0, 3);
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

    private bool ShouldShowUncollectedWarning() =>
        paperInvoice && SelectedReceiptState() == PaperInvoiceReceiptStates.Uncollected;

    private void UpdateUncollectedWarning()
    {
        uncollectedWarning.Visible = ShouldShowUncollectedWarning();
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
        panel.Controls.Add(FieldLabel(label), 0, row);
        panel.Controls.Add(field, 1, row);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 8, 0),
    };

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
            invoiceNumber.TextAlign != HorizontalAlignment.Left || employeeNo.TextAlign != HorizontalAlignment.Left ||
            password.TextAlign != HorizontalAlignment.Left || ClientSize.Width != WindowWidth ||
            ClientSize.Height != CalculateHeight() || AcceptButton is not null || CancelButton != cancel ||
            !password.UseSystemPasswordChar)
            throw new InvalidOperationException("發票作廢確認視窗基本配置不正確");
        if (paperInvoice && receiptState.Items.Count != 3)
            throw new InvalidOperationException("紙本發票證明聯狀態選項不完整");
        if (!paperInvoice && receiptState.Parent is not null)
            throw new InvalidOperationException("非紙本發票不應顯示證明聯狀態");
        if (paperInvoice)
        {
            if (uncollectedWarning.Text != UncollectedWarningText)
                throw new InvalidOperationException("尚未收回提示文字不正確");
            receiptState.SelectedIndex = 2;
            if (!ShouldShowUncollectedWarning())
                throw new InvalidOperationException("選擇尚未收回後未進入管理員確認提示狀態");
        }
    }

    private sealed record ReceiptChoice(string Text, string Value)
    {
        public override string ToString() => Text;
    }
}
