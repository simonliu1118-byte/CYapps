namespace CYERPAutoInput;

internal static class CyVisualTheme
{
    internal static readonly Color Window = Color.FromArgb(248, 250, 252);
    internal static readonly Color White = Color.White;
    internal static readonly Color ReadOnly = Color.FromArgb(241, 243, 245);
    internal static readonly Color Border = Color.FromArgb(209, 213, 219);
    internal static readonly Color Grid = Color.FromArgb(221, 225, 230);
    internal static readonly Color TextPrimary = Color.FromArgb(31, 41, 55);
    internal static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
    internal static readonly Color TextDisabled = Color.FromArgb(152, 162, 179);
    internal static readonly Color Accent = Color.FromArgb(216, 132, 74);
    internal static readonly Color AccentHover = Color.FromArgb(195, 115, 63);
    internal static readonly Color AccentPressed = Color.FromArgb(170, 100, 53);
    internal static readonly Color AccentSoft = Color.FromArgb(250, 236, 221);
    internal static readonly Color AccentFocus = Color.FromArgb(224, 161, 120);
    internal static readonly Color Selection = Color.FromArgb(246, 227, 209);

    public static void Apply(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = TextPrimary;
        ApplyControlTree(form);
    }

    private static void ApplyControlTree(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case DataGridView grid:
                    ApplyGrid(grid);
                    break;
                case TextBox textBox:
                    textBox.BackColor = textBox.ReadOnly ? ReadOnly : White;
                    textBox.ForeColor = TextPrimary;
                    break;
                case GroupBox groupBox:
                    groupBox.BackColor = Window;
                    groupBox.ForeColor = TextPrimary;
                    break;
                case TableLayoutPanel table:
                    table.BackColor = Window;
                    table.ForeColor = TextPrimary;
                    break;
                case FlowLayoutPanel flow:
                    flow.BackColor = Window;
                    flow.ForeColor = TextPrimary;
                    break;
                case Panel panel:
                    panel.BackColor = Window;
                    panel.ForeColor = TextPrimary;
                    break;
                case StatusStrip statusStrip:
                    statusStrip.BackColor = Window;
                    statusStrip.ForeColor = TextSecondary;
                    break;
                case Label label:
                    label.ForeColor = TextPrimary;
                    label.BackColor = Window;
                    break;
                case RadioButton radio:
                    radio.BackColor = Window;
                    radio.ForeColor = TextPrimary;
                    break;
                case CheckBox checkBox when checkBox is not ModeToggle:
                    checkBox.BackColor = Window;
                    checkBox.ForeColor = TextPrimary;
                    break;
            }

            if (control.HasChildren)
                ApplyControlTree(control);
        }
    }

    private static void ApplyGrid(DataGridView grid)
    {
        grid.BackgroundColor = White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = Grid;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 30;
        grid.RowTemplate.Height = 28;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Window;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Window;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = White;
        grid.DefaultCellStyle.ForeColor = TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
        grid.DefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Regular);
        grid.RowHeadersDefaultCellStyle.BackColor = Window;
        grid.RowHeadersDefaultCellStyle.ForeColor = TextSecondary;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
    }
}
