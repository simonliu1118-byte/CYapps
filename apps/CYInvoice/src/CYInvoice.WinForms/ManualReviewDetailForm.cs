namespace CYInvoice.WinForms;

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

    public ManualReviewDetailForm(string title, IEnumerable<KeyValuePair<string, string>> rows)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(570, 430);
        MinimumSize = new Size(500, 360);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

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
        close.DialogResult = DialogResult.OK;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(close);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(scroller, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
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
}
