using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class VoidConfirmationForm : Form
{
    private const string ResponsibilityText =
        "本人已核對本次作廢之發票號碼、交易事實、作廢原因及紙本電子發票證明聯收回狀況。\r\n\r\n本人了解發票作廢應依相關法令及公司作業規範辦理；如因本人故意、操作錯誤或疏失致生相關問題，願依實際責任歸屬、相關法令及公司規定負應負之責任。\r\n\r\n按下「確認作廢」即表示本人已閱讀、理解並同意上述聲明。";
    private const string UncollectedWarningText = "尚未收回：需由管理員確認後才會送出作廢";
    private const int WindowWidth = 560;
    private const int PaperWindowHeight = 390;
    private const int CarrierWindowHeight = 246;
    private const int InputColumnWidth = 305;
    private const int DividerWidth = 1;
    private const int FieldRowHeight = 34;
    private const int ReceiptSectionHeight = 208;
    private const int ReceiptGapHeight = 18;
    private const int ActionRowHeight = 44;
    private const int CompactButtonWidth = 104;

    private readonly bool paperInvoice;
    private readonly TextBox invoiceNumber = UiControls.TextBox(10);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(200);
    private readonly RadioButton notDelivered = ReceiptButton("未列印 / 未交付");
    private readonly RadioButton collected = ReceiptButton("已收回");
    private readonly RadioButton uncollected = ReceiptButton("尚未收回");
    private readonly Label uncollectedWarning = new()
    {
        Text = UncollectedWarningText,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(185, 108, 0),
        Visible = false,
        Margin = Padding.Empty,
        Font = new Font("Microsoft JhengHei UI", 8.5F),
    };
    private readonly Button confirm = UiControls.DangerButton("確認作廢");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public VoidConfirmationForm(bool paperInvoice)
    {
        this.paperInvoice = paperInvoice;
        Text = "發票作廢確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, paperInvoice ? PaperWindowHeight : CarrierWindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
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
        ConfigureInputField(invoiceNumber);
        ConfigureInputField(employeeNo);
        ConfigureInputField(password);
        invoiceNumber.CharacterCasing = CharacterCasing.Upper;
        confirm.Width = CompactButtonWidth;
        cancel.Width = CompactButtonWidth;

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = paperInvoice ? 3 : 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 3 * FieldRowHeight));
        left.Controls.Add(BuildFields(), 0, 0);
        if (paperInvoice)
        {
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, ReceiptGapHeight));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, ReceiptSectionHeight));
            left.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
            left.Controls.Add(BuildReceiptStateHost(), 0, 2);
        }

        var divider = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(190, 190, 190),
            Margin = Padding.Empty,
        };

        var statementHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(12, 0, 2, 0),
        };
        var responsibility = new Label
        {
            Text = ResponsibilityText,
            AutoSize = true,
            MaximumSize = new Size(205, 0),
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.FromArgb(65, 65, 65),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Margin = Padding.Empty,
            Tag = "void-responsibility",
        };
        statementHost.Controls.Add(responsibility);

        confirm.Click += (_, _) => Submit();
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(confirm);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(12, 10, 12, 8),
            Margin = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InputColumnWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DividerWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(divider, 1, 0);
        root.Controls.Add(statementHost, 2, 0);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 3);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private Control BuildFields()
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        AddField(fields, "發票號碼", invoiceNumber, 0);
        AddField(fields, "員工編號", employeeNo, 1);
        AddField(fields, "員工密碼", password, 2);

        invoiceNumber.TabIndex = 0;
        employeeNo.TabIndex = 1;
        password.TabIndex = 2;
        invoiceNumber.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, employeeNo);
        employeeNo.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, password);
        password.KeyDown += (_, eventArgs) => AdvanceOnEnter(eventArgs, paperInvoice ? notDelivered : confirm);
        return fields;
    }

    private Control BuildReceiptStateHost()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 8, 0),
        };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.Controls.Add(new Label
        {
            Text = "證明聯狀態",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);
        host.Controls.Add(notDelivered, 0, 1);
        host.Controls.Add(collected, 0, 2);
        host.Controls.Add(uncollected, 0, 3);
        host.Controls.Add(uncollectedWarning, 0, 4);

        notDelivered.TabIndex = 3;
        collected.TabIndex = 4;
        uncollected.TabIndex = 5;
        uncollected.CheckedChanged += (_, _) => UpdateUncollectedWarning();
        return host;
    }

    private static RadioButton ReceiptButton(string text) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize = false,
        Margin = new Padding(0, 2, 0, 2),
        UseVisualStyleBackColor = true,
        Font = new Font("Microsoft JhengHei UI", 9F),
    };

    private static void ConfigureInputField(TextBox field)
    {
        field.Dock = DockStyle.None;
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(3, 0, 3, 0);
        field.TextAlign = HorizontalAlignment.Left;
    }

    private bool ShouldShowUncollectedWarning() => paperInvoice && uncollected.Checked;

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
        if (paperInvoice && SelectedReceiptState().Length == 0)
        {
            MessageBox.Show(this, "請選擇證明聯狀態。", "資料未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            notDelivered.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private string SelectedReceiptState()
    {
        if (notDelivered.Checked) return PaperInvoiceReceiptStates.NotPrintedOrNotDelivered;
        if (collected.Checked) return PaperInvoiceReceiptStates.Collected;
        if (uncollected.Checked) return PaperInvoiceReceiptStates.Uncollected;
        return string.Empty;
    }

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
        Margin = new Padding(0, 0, 6, 0),
        AutoEllipsis = false,
    };

    private static void AdvanceOnEnter(KeyEventArgs eventArgs, Control next)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        next.Focus();
    }

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 5, 0, 0);
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
        var responsibility = FindTaggedResponsibility(this);
        if (Text != "發票作廢確認" || ShowIcon || invoiceNumber.Text.Length != 0 || password.Text.Length != 0 ||
            invoiceNumber.TextAlign != HorizontalAlignment.Left || employeeNo.TextAlign != HorizontalAlignment.Left ||
            password.TextAlign != HorizontalAlignment.Left || ClientSize.Width != WindowWidth ||
            ClientSize.Height != (paperInvoice ? PaperWindowHeight : CarrierWindowHeight) || AcceptButton is not null || CancelButton != cancel ||
            !password.UseSystemPasswordChar || confirm.FlatStyle != FlatStyle.Standard || confirm.UseVisualStyleBackColor ||
            confirm.BackColor != Color.FromArgb(183, 28, 28) || confirm.ForeColor != Color.White ||
            responsibility is null || !responsibility.Font.Bold || !responsibility.Text.Contains("同意上述聲明", StringComparison.Ordinal))
            throw new InvalidOperationException("發票作廢確認視窗基本配置不正確");
        if (paperInvoice)
        {
            if (notDelivered.Appearance != Appearance.Button || collected.Appearance != Appearance.Button || uncollected.Appearance != Appearance.Button ||
                !(notDelivered.Top < collected.Top && collected.Top < uncollected.Top))
                throw new InvalidOperationException("紙本發票證明聯狀態未使用垂直三選一按鈕");
            if (uncollectedWarning.Text != UncollectedWarningText)
                throw new InvalidOperationException("尚未收回提示文字不正確");
            uncollected.Checked = true;
            if (!ShouldShowUncollectedWarning())
                throw new InvalidOperationException("選擇尚未收回後未進入管理員確認提示狀態");
        }
        else if (notDelivered.Parent is not null)
        {
            throw new InvalidOperationException("非紙本發票不應顯示證明聯狀態");
        }
        VoidConfirmationPrivacyMask.VerifySmokeLayout();
    }

    private static Label? FindTaggedResponsibility(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label && Equals(label.Tag, "void-responsibility")) return label;
            var nested = FindTaggedResponsibility(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
