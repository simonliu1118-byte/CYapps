namespace CYERPAutoInput;

/// <summary>One part of a batch's closing list: a heading and its lines (left out when empty).</summary>
internal sealed record BatchSummarySection(string Heading, IReadOnlyList<string> Items);

/// <summary>
/// The closing list of every batch (user, 2026-10-13: 全自動, MO店+, 酷澎 and 蝦皮 imports all
/// open the same list, only the data differs): a headline, what was done, what needs a person,
/// where it stopped. It scrolls and copies, because it can be long.
/// </summary>
internal sealed class BatchSummaryForm : Form
{
    public BatchSummaryForm(string title, string headline, IEnumerable<BatchSummarySection> sections, string? stoppedAt = null)
    {
        var parts = sections.Where(s => s.Items.Count > 0).ToList();
        var clean = stoppedAt is null && parts.All(p => p.Heading.StartsWith("已完成", StringComparison.Ordinal));
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, clean ? 300 : 520);
        MinimumSize = new Size(480, 240);
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Font = new Font("Microsoft JhengHei UI", 10F);

        var lines = new List<string> { headline };
        foreach (var part in parts)
        {
            lines.Add(string.Empty);
            lines.Add(part.Heading);
            lines.AddRange(part.Items.Select(i => "・" + i.Replace("\n", "\r\n    ")));
        }
        if (stoppedAt is not null)
        {
            lines.Add(string.Empty);
            lines.Add(stoppedAt);
        }
        var text = string.Join(Environment.NewLine, lines);

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = text,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50, Padding = new Padding(8) };
        var ok = new CyPrimaryButton { Text = "確定", Size = new Size(92, 34), DialogResult = DialogResult.OK };
        var copy = new CyButton { Text = "複製", Size = new Size(92, 34) };
        copy.Click += (_, _) => Clipboard.SetText(text);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(copy);
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 4) };
        pad.Controls.Add(box);
        Controls.Add(pad);
        Controls.Add(buttons);
        AcceptButton = ok;
        CyVisualTheme.Apply(this);
        Shown += (_, _) => box.Select(0, 0);
    }
}
