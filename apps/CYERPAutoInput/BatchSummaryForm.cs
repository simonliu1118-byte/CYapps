namespace CYERPAutoInput;

/// <summary>
/// The closing list of every batch (user, 2026-10-13: 全自動, MO店+, 酷澎 and 蝦皮 imports all
/// open the same list, only the data differs): a headline, then a table (user, 2026-10-15:
/// easier to read than plain text) of what was done and what needs a person — 狀態, 訂單編號,
/// 說明, the rows for a person first and in orange — and where it stopped. 複製 gives the table
/// as columns for Excel.
/// </summary>
internal sealed class BatchSummaryForm : Form
{
    public BatchSummaryForm(string title, string headline, IEnumerable<BatchSummarySection> sections, string? stoppedAt = null)
    {
        var rows = BatchSummary.Rows(sections).OrderBy(r => r.NeedsPerson ? 0 : 1).ToList();
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        // Opens over Chrome／ERP, which the batch left in front.
        TopMost = true;
        Size = new Size(900, rows.Count > 6 || stoppedAt is not null ? 560 : 400);
        MinimumSize = new Size(560, 300);
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Font = new Font("Microsoft JhengHei UI", 10F);

        var head = new Label
        {
            Text = headline,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            ShowItemToolTips = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };
        list.Columns.Add("狀態", 150);
        list.Columns.Add("訂單編號", 170);
        list.Columns.Add("說明", 520);
        foreach (var row in rows)
        {
            var item = new ListViewItem([row.Status, row.OrderSn, row.Detail]) { ToolTipText = row.Detail };
            if (row.NeedsPerson) item.ForeColor = CyVisualTheme.Warning;
            list.Items.Add(item);
        }
        if (rows.Count == 0) list.Items.Add(new ListViewItem(["", "", "沒有需要列出的項目。"]));
        // 說明 takes the rest of the width.
        void Fit() => list.Columns[2].Width = Math.Max(200, list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width - 4);
        list.Resize += (_, _) => Fit();

        var stopped = new Label
        {
            Text = stoppedAt ?? string.Empty,
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = stoppedAt is null ? 0 : 60,
            ForeColor = CyVisualTheme.Danger,
            Padding = new Padding(0, 8, 0, 0)
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50, Padding = new Padding(8) };
        var ok = new CyPrimaryButton { Text = "確定", Size = new Size(92, 34), DialogResult = DialogResult.OK };
        var copy = new CyButton { Text = "複製", Size = new Size(92, 34) };
        copy.Click += (_, _) => Clipboard.SetText(BatchSummary.Tsv(headline, rows, stoppedAt));
        buttons.Controls.Add(ok);
        buttons.Controls.Add(copy);
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 4) };
        pad.Controls.Add(list);
        pad.Controls.Add(stopped);
        pad.Controls.Add(head);
        Controls.Add(pad);
        Controls.Add(buttons);
        AcceptButton = ok;
        CyVisualTheme.Apply(this);
        Shown += (_, _) => Fit();
    }
}
