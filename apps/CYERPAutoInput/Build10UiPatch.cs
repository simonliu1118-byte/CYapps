namespace CYERPAutoInput;

internal static class Build10UiPatch
{
    private static readonly Size StandardWindowSize = new(1160, 720);

    public static void Apply(MainForm form)
    {
        form.MinimumSize = StandardWindowSize;
        if (form.Width < StandardWindowSize.Width || form.Height < StandardWindowSize.Height)
            form.Size = StandardWindowSize;

        var root = Descendants(form).OfType<TableLayoutPanel>()
            .FirstOrDefault(x => x.RowCount == 4 && x.RowStyles.Count >= 4);

        foreach (var group in Descendants(form).OfType<GroupBox>())
        {
            if (group.Text.StartsWith("發票資料", StringComparison.Ordinal))
                group.Text = "發票資料";
        }

        var grid = Descendants(form).OfType<DetailDataGridView>().FirstOrDefault();
        if (grid is not null)
            ConfigureDetailGrid(grid);

        foreach (var strip in Descendants(form).OfType<StatusStrip>())
        {
            foreach (var item in strip.Items.OfType<ToolStripStatusLabel>())
            {
                if (item.Text?.StartsWith("V0.1.0 Build", StringComparison.Ordinal) == true)
                    item.Text = "V0.1.0 Build 18 · Esc：緊急停止 · 不自動儲存 ERP";
            }
        }

        var toggle = Descendants(form).OfType<ModeToggle>().FirstOrDefault();
        if (toggle is not null)
        {
            toggle.CheckedChanged += (_, _) => ApplyModeWindow(form, root, toggle.Checked);
            ApplyModeWindow(form, root, toggle.Checked);
        }
        else if (root is not null)
        {
            ApplyStandardRows(root);
        }
    }

    private static void ApplyModeWindow(Form form, TableLayoutPanel? root, bool advanced)
    {
        if (root is not null)
        {
            if (advanced)
            {
                root.RowStyles[1].SizeType = SizeType.Percent;
                root.RowStyles[1].Height = 100;
                root.RowStyles[2].SizeType = SizeType.Absolute;
                root.RowStyles[2].Height = 330;
            }
            else
            {
                ApplyStandardRows(root);
            }
            root.PerformLayout();
        }

        if (advanced)
        {
            form.WindowState = FormWindowState.Maximized;
        }
        else
        {
            form.WindowState = FormWindowState.Normal;
            form.Size = StandardWindowSize;
        }
    }

    private static void ApplyStandardRows(TableLayoutPanel root)
    {
        root.RowStyles[1].SizeType = SizeType.Absolute;
        root.RowStyles[1].Height = 252;
        root.RowStyles[2].SizeType = SizeType.Percent;
        root.RowStyles[2].Height = 100;
    }

    private static void ConfigureDetailGrid(DetailDataGridView grid)
    {
        grid.RowHeadersVisible = false;
        grid.ScrollBars = ScrollBars.Vertical;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.ColumnHeadersHeight = 29;
        grid.RowTemplate.Height = 27;

        if (!grid.Columns.Contains("Sequence"))
        {
            var sequence = new DataGridViewTextBoxColumn
            {
                Name = "Sequence",
                HeaderText = "序號",
                Width = 55,
                ReadOnly = true,
                Frozen = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };
            grid.Columns.Insert(0, sequence);
        }

        grid.RowsDefaultCellStyle.BackColor = CyVisualTheme.White;
        grid.RowsDefaultCellStyle.ForeColor = CyVisualTheme.TextPrimary;
        grid.RowsDefaultCellStyle.SelectionBackColor = CyVisualTheme.Selection;
        grid.RowsDefaultCellStyle.SelectionForeColor = CyVisualTheme.TextPrimary;
        grid.AlternatingRowsDefaultCellStyle.BackColor = CyVisualTheme.Window;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = CyVisualTheme.TextPrimary;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = CyVisualTheme.Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = CyVisualTheme.TextPrimary;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow) row.Height = 27;
        }

        var dataRows = grid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
        while (dataRows < 10)
        {
            grid.Rows.Add();
            dataRows++;
        }

        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!grid.Columns[e.ColumnIndex].Name.Equals("Sequence", StringComparison.Ordinal)) return;
            e.Value = (e.RowIndex + 1).ToString();
            e.FormattingApplied = true;
        };
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
