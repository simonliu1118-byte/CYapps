namespace CYInvoice.WinForms;

internal static class UiControls
{
    public static Label Label(string text, ContentAlignment alignment = ContentAlignment.MiddleLeft) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = alignment, AutoEllipsis = true, Margin = new Padding(3),
    };

    public static TextBox TextBox(int maximumLength = 32767) => new()
    {
        Dock = DockStyle.Fill, MaxLength = maximumLength, Margin = new Padding(3, 5, 3, 5),
    };

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false, AllowUserToResizeColumns = false, AllowUserToResizeRows = false,
            AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, ColumnHeadersHeight = 28,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false, GridColor = Color.FromArgb(226, 226, 226), RowHeadersVisible = false,
            ScrollBars = ScrollBars.Vertical,
        };
        grid.RowTemplate.Height = 27;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(246, 246, 246);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(246, 246, 246);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        grid.RowsDefaultCellStyle.BackColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 247);
        return grid;
    }

    public static void ReserveVerticalScrollBar(DataGridView grid, int flexibleColumnIndex)
    {
        void LayoutColumns()
        {
            if (flexibleColumnIndex < 0 || flexibleColumnIndex >= grid.Columns.Count) return;
            var fixedWidth = grid.Columns.Cast<DataGridViewColumn>().Where((_, index) => index != flexibleColumnIndex).Sum(column => column.Width);
            var available = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - fixedWidth - 3;
            grid.Columns[flexibleColumnIndex].Width = Math.Max(grid.Columns[flexibleColumnIndex].MinimumWidth, available);
        }
        grid.Layout += (_, _) => LayoutColumns();
        grid.SizeChanged += (_, _) => LayoutColumns();
    }
}
