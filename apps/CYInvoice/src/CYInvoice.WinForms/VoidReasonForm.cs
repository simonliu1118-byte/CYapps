using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class VoidReasonForm : Form
{
    private const int WindowWidth = 280;
    private const int WindowHeight = 116;
    private const int CompactButtonWidth = 100;
    private readonly TextBox reason = UiControls.TextBox(EmployeeVoidWorkflowService.MaxReasonLength);
    private readonly Label counter = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Color.DimGray,
        Margin = Padding.Empty,
    };
    private readonly Button next = CompactButton("下一步");
    private readonly Button cancel = CompactButton("取消");

    public VoidReasonForm()
    {
        Text = "發票作廢";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(WindowWidth, WindowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BuildLayout();
        UpdateCounter();
        Shown += (_, _) => reason.Focus();
    }

    public string Reason => reason.Text.Trim();

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10, 7, 10, 7),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        header.Controls.Add(new Label
        {
            Text = $"作廢原因 (最多 {EmployeeVoidWorkflowService.MaxReasonLength} 字)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        }, 0, 0);
        header.Controls.Add(counter, 1, 0);
        root.Controls.Add(header, 0, 0);

        reason.Dock = DockStyle.None;
        reason.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        reason.Margin = Padding.Empty;
        reason.TextAlign = HorizontalAlignment.Left;
        reason.TabIndex = 0;
        reason.TextChanged += (_, _) => UpdateCounter();
        reason.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            next.PerformClick();
        };
        root.Controls.Add(reason, 0, 1);

        next.Click += (_, _) => Submit();
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 3, 0, 0),
        };
        buttons.SizeChanged += (_, _) => CenterButtons(buttons);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(next);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void UpdateCounter()
    {
        var length = reason.Text.EnumerateRunes().Count();
        counter.Text = $"{length}/{EmployeeVoidWorkflowService.MaxReasonLength}";
    }

    private void Submit()
    {
        var value = reason.Text.Trim();
        var length = value.EnumerateRunes().Count();
        if (length == 0)
        {
            ValidationError("請輸入作廢原因。");
            return;
        }
        if (length > EmployeeVoidWorkflowService.MaxReasonLength)
        {
            ValidationError($"作廢原因最多 {EmployeeVoidWorkflowService.MaxReasonLength} 字。");
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ValidationError(string message)
    {
        MessageBox.Show(this, message, "資料未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        BeginInvoke((Action)(() =>
        {
            reason.Focus();
            reason.SelectAll();
        }));
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "發票作廢" || ShowIcon || AcceptButton is not null || CancelButton != cancel ||
            reason.TextAlign != HorizontalAlignment.Left || ClientSize.Width != WindowWidth || ClientSize.Height != WindowHeight ||
            reason.MaxLength != EmployeeVoidWorkflowService.MaxReasonLength ||
            counter.Text != $"0/{EmployeeVoidWorkflowService.MaxReasonLength}")
            throw new InvalidOperationException("作廢原因視窗基本配置不正確");
        if (!UiControls.HasLogicalSize(next, CompactButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(cancel, CompactButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("作廢原因按鈕尺寸不正確");
    }

    private static Button CompactButton(string text) => new NoFocusCueButton
    {
        Text = text,
        Width = CompactButtonWidth,
        Height = UiControls.StandardButtonHeight,
        Margin = new Padding(5, 2, 5, 2),
        AutoSize = false,
        UseVisualStyleBackColor = true,
    };

    private static void CenterButtons(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 3, 0, 0);
    }
}
