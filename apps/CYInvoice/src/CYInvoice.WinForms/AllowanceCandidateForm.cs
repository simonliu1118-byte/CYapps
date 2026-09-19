using CYInvoice.Core;
using CYInvoice.Core.Amego;

namespace CYInvoice.WinForms;

internal sealed class AllowanceCandidateForm : Form
{
    private readonly ComboBox candidates = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(0, 4, 0, 4),
    };
    private readonly Button confirm = UiControls.StandardButton("確認");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public AllowanceCandidateForm(IReadOnlyList<InvoiceAllowanceResult> values)
    {
        if (values is null || values.Count == 0)
            throw new ArgumentException("at least one allowance candidate is required", nameof(values));

        Text = "確認折讓單號";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        foreach (var value in values
                     .Where(item => item.AllowanceNumber.Trim().Length != 0)
                     .GroupBy(item => item.AllowanceNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            candidates.Items.Add(new CandidateItem(value, Display(value)));
        }
        if (candidates.Items.Count == 0)
            throw new InvalidDataException("折讓候選資料缺少折讓單號");
        candidates.SelectedIndex = 0;

        confirm.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.DialogResult = DialogResult.Cancel;
        BuildLayout();
    }

    public string SelectedAllowanceNumber =>
        candidates.SelectedItem is CandidateItem item ? item.Value.AllowanceNumber.Trim() : string.Empty;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(new Label
        {
            Text = "請確認哪一筆是本次人工折讓：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        }, 0, 0);
        root.Controls.Add(candidates, 0, 1);
        root.Controls.Add(new Label
        {
            Text = "系統只在無法自動唯一辨認時要求人工確認。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Margin = Padding.Empty,
        }, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(confirm);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        AcceptButton = confirm;
        CancelButton = cancel;
    }

    private static string Display(InvoiceAllowanceResult value)
    {
        var amount = "?";
        try
        {
            amount = FixedDecimal.Add(
                FixedDecimal.Parse(value.TotalAmount),
                FixedDecimal.Parse(value.TaxAmount)).ToString();
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
        }
        return $"{value.AllowanceNumber}｜{value.AllowanceDate}｜{value.InvoiceType}｜狀態 {value.InvoiceStatus}｜含稅 {amount}";
    }

    private sealed record CandidateItem(InvoiceAllowanceResult Value, string Text)
    {
        public override string ToString() => Text;
    }
}
