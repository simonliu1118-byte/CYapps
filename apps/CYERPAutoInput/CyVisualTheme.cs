namespace CYERPAutoInput;

internal static class CyVisualTheme
{
    private static readonly Color Window = Color.FromArgb(248, 250, 252);
    private static readonly Color White = Color.White;
    private static readonly Color Border = Color.FromArgb(209, 213, 219);
    private static readonly Color Grid = Color.FromArgb(221, 225, 230);
    private static readonly Color TextPrimary = Color.FromArgb(31, 41, 55);
    private static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
    private static readonly Color TextDisabled = Color.FromArgb(152, 162, 179);
    private static readonly Color Accent = Color.FromArgb(216, 132, 74);
    private static readonly Color AccentHover = Color.FromArgb(195, 115, 63);
    private static readonly Color AccentPressed = Color.FromArgb(170, 100, 53);
    private static readonly Color AccentSoft = Color.FromArgb(250, 236, 221);
    private static readonly Color Selection = Color.FromArgb(246, 227, 209);

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
                    textBox.BackColor = White;
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
                case Label label:
                    ApplyLabel(label);
                    break;
                case RadioButton radio:
                    radio.BackColor = Window;
                    radio.ForeColor = TextPrimary;
                    break;
                case CheckBox checkBox:
                    checkBox.BackColor = Window;
                    checkBox.ForeColor = TextPrimary;
                    break;
                case Button button:
                    ApplyButton(button);
                    break;
            }

            if (control.HasChildren)
                ApplyControlTree(control);
        }
    }

    private static void ApplyLabel(Label label)
    {
        label.ForeColor = TextPrimary;
        label.BackColor = Window;

        if (label.Text.StartsWith("V0.1.0 Build", StringComparison.Ordinal) ||
            label.Text.StartsWith("ERP：", StringComparison.Ordinal))
        {
            label.ForeColor = TextSecondary;
        }
    }

    private static void ApplyButton(Button button)
    {
        if (!button.Text.Equals("開始輸入 ERP", StringComparison.Ordinal))
            return;

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Accent;
        button.ForeColor = White;

        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled) button.BackColor = AccentHover;
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Enabled) button.BackColor = Accent;
        };
        button.MouseDown += (_, _) =>
        {
            if (button.Enabled) button.BackColor = AccentPressed;
        };
        button.MouseUp += (_, _) =>
        {
            if (button.Enabled) button.BackColor = button.ClientRectangle.Contains(button.PointToClient(Cursor.Position))
                ? AccentHover
                : Accent;
        };
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? Accent : AccentSoft;
            button.ForeColor = button.Enabled ? White : TextDisabled;
        };
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
