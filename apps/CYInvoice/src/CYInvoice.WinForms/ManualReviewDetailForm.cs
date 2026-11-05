using CYInvoice.Core;

namespace CYInvoice.WinForms;

internal enum ManualReviewDetailAction
{
    None,
    Primary,
    Secondary,
    CancelReturn,
    AdministrativeClose,
}

internal sealed class ManualReviewDetailForm : Form
{
    private BusinessDetailControl content = null!;
    private TableLayoutPanel layout = null!;
    public ManualReviewDetailForm(
        string title,
        IEnumerable<KeyValuePair<string, string>> rows,
        string primaryActionText = "",
        bool allowCancelReturn = false,
        bool allowAdministrativeClose = false,
        bool primaryDanger = false,
        string secondaryActionText = "",
        IReadOnlyList<InvoiceItem>? originalItems = null,
        IReadOnlyList<BusinessDetailTable>? tables = null)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 640);
        MinimumSize = new Size(500, 360);
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        var fields = rows.ToArray();
        var allTables = (tables ?? []).ToList();
        if (originalItems is not null) {
            fields = fields.Where(row => row.Key != "原發票商品").ToArray();
            allTables.Insert(0, BusinessDetailControl.Products("原發票商品", originalItems));
        }
        content = new BusinessDetailControl(fields, allTables);

        var close = UiControls.StandardButton("關閉");
        close.Width = 96;
        close.DialogResult = DialogResult.Cancel;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
            Margin = Padding.Empty,
        };

        if (allowAdministrativeClose)
        {
            var administrativeClose = UiControls.StandardButton("管理員結案");
            administrativeClose.Width = 112;
            administrativeClose.Click += (_, _) => Complete(ManualReviewDetailAction.AdministrativeClose);
            actions.Controls.Add(administrativeClose);
        }
        if (allowCancelReturn)
        {
            var cancelReturn = UiControls.StandardButton("取消退回");
            cancelReturn.Width = 100;
            cancelReturn.TabIndex = 1;
            cancelReturn.Click += (_, _) => Complete(ManualReviewDetailAction.CancelReturn);
            actions.Controls.Add(cancelReturn);
        }
        if (!string.IsNullOrWhiteSpace(primaryActionText))
        {
            var useDangerStyle = primaryDanger ||
                                 string.Equals(primaryActionText, "已人工處理", StringComparison.Ordinal);
            var primary = useDangerStyle
                ? UiControls.DangerButton(primaryActionText)
                : UiControls.StandardButton(primaryActionText);
            primary.Width = primaryActionText.Length >= 7 ? 132 : 112;
            primary.TabIndex = 0;
            primary.Click += (_, _) => Complete(ManualReviewDetailAction.Primary);
            actions.Controls.Add(primary);
        }
        if (!string.IsNullOrWhiteSpace(secondaryActionText))
        {
            var secondary = UiControls.StandardButton(secondaryActionText);
            secondary.Width = 112;
            secondary.Click += (_, _) => Complete(ManualReviewDetailAction.Secondary);
            actions.Controls.Add(secondary);
        }
        actions.Controls.Add(close);
        actions.SizeChanged += (_, _) => CenterActions(actions);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout = root;
        root.Controls.Add(content, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
    }

    public ManualReviewDetailAction SelectedAction { get; private set; }

    public void UpdateContent(IReadOnlyList<KeyValuePair<string,string>> fields,IReadOnlyList<BusinessDetailTable> tables)
    {
        var position=content.AutoScrollPosition;
        layout.SuspendLayout();
        layout.Controls.Remove(content);
        content.Dispose();
        content=new BusinessDetailControl(fields,tables);
        layout.Controls.Add(content,0,0);
        layout.ResumeLayout(true);
        content.AutoScrollPosition=new Point(-position.X,-position.Y);
    }

    private void Complete(ManualReviewDetailAction action)
    {
        SelectedAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void CenterActions(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 4, 0, 0);
    }
}
