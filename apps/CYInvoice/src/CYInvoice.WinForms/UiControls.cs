using System.Runtime.InteropServices;

namespace CYInvoice.WinForms;

internal class NoFocusCueButton : Button
{
    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        UiControls.HideFocusCue(this);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        UiControls.HideFocusCue(this);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        UiControls.HideFocusCue(this);
    }
}

internal sealed class NoFocusCueTabControl : TabControl
{
    public NoFocusCueTabControl() => TabStop = false;

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        UiControls.HideFocusCue(this);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        UiControls.HideFocusCue(this);
    }
}

internal static class UiControls
{
    private const int WmUpdateUiState = 0x0128;
    private static readonly IntPtr HideFocusState = new(0x00010001);

    internal static void HideFocusCue(Control control)
    {
        if (control.IsHandleCreated)
            SendMessage(control.Handle, WmUpdateUiState, HideFocusState, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    public static Label Label(string text, ContentAlignment alignment = ContentAlignment.MiddleLeft) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = alignment, AutoEllipsis = true, Margin = new Padding(3),
    };

    public static TextBox TextBox(int maximumLength = 32767) => new()
    {
        Dock = DockStyle.Fill, MaxLength = maximumLength, Margin = new Padding(3, 5, 3, 5),
    };

    public const int StandardButtonWidth = 132;
    public const int StandardButtonHeight = 34;

    public static Button StandardButton(string text) => new NoFocusCueButton()
    {
        Text = text,
        Width = StandardButtonWidth,
        Height = StandardButtonHeight,
        Margin = new Padding(6, 2, 6, 2),
        AutoSize = false,
        UseVisualStyleBackColor = true,
    };

    public static Button ImportButton(string text, ImportBrand brand) => new ImportBrandButton(text, brand);

    public static Button PrimaryIssueButton() => new PrimaryActionButton();

    public static void ApplyIssueButtonTheme(Button button, bool production)
    {
        if (button is PrimaryActionButton primary)
        {
            primary.SetEnvironment(production);
            return;
        }

        button.BackColor = production ? Color.FromArgb(3, 155, 229) : Color.FromArgb(25, 135, 84);
    }

    public static bool HasLogicalSize(Control control, int width, int height, int tolerance = 3)
    {
        var dpi = control.DeviceDpi > 0 ? control.DeviceDpi : 96;
        var logicalWidth = control.Width * 96D / dpi;
        var logicalHeight = control.Height * 96D / dpi;
        return Math.Abs(logicalWidth - width) <= tolerance && Math.Abs(logicalHeight - height) <= tolerance;
    }

    public static void SetTextBoxLocked(TextBox field, bool locked)
    {
        field.Enabled = true;
        field.ReadOnly = locked;
        field.TabStop = !locked;
        field.BackColor = locked ? Color.FromArgb(242, 242, 242) : Color.White;
        field.BorderStyle = BorderStyle.FixedSingle;
    }

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
