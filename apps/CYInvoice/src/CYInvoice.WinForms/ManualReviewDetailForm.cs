namespace CYInvoice.WinForms;

internal enum ManualReviewDetailAction
{
    None,
    Primary,
    CancelReturn,
    AdministrativeClose,
}

internal sealed class ManualReviewDetailForm : Form
{
    private readonly TableLayoutPanel details = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        RowCount = 0,
        Margin = Padding.Empty,
    };

    public ManualReviewDetailForm(
        string title,
        IEnumerable<KeyValuePair<string, string>> rows,
        string primaryActionText = "",
        bool allowCancelReturn = false,
        bool allowAdministrativeClose = false,
        bool primaryDanger = false)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(570, 430);
        MinimumSize = new Size(500, 360);
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var row in rows) AddRow(row.Key, row.Value);

        var scroller = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Padding = new Padding(10),
            Margin = Padding.Empty,
        };
        scroller.Controls.Add(details);

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
            cancelReturn.Click += (_, _) => Complete(ManualReviewDetailAction.CancelReturn);
            actions.Controls.Add(cancelReturn);
        }
        if (!string.IsNullOrWhiteSpace(primaryActionText))
        {
            var primary = primaryDanger
                ? UiControls.DangerButton(primaryActionText)
                : UiControls.StandardButton(primaryActionText);
            primary.Width = primaryActionText.Length >= 7 ? 132 : 112;
            primary.Click += (_, _) => Complete(ManualReviewDetailAction.Primary);
            actions.Controls.Add(primary);
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
        root.Controls.Add(scroller, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = close;
    }

    public ManualReviewDetailAction SelectedAction { get; private set; }

    private void Complete(ManualReviewDetailAction action)
    {
        SelectedAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddRow(string label, string value)
    {
        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 5),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 70, 70),
        }, 0, row);
        details.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(value) ? "－" : value.Trim(),
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Margin = new Padding(0, 5, 0, 5),
            ForeColor = SystemColors.ControlText,
        }, 1, row);
    }

    private static void CenterActions(FlowLayoutPanel panel)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2), 4, 0, 0);
    }
}
