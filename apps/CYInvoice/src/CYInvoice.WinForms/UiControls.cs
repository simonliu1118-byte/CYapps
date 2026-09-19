using System.Drawing.Drawing2D;
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

internal sealed class DangerActionButton : NoFocusCueButton
{
    private bool mouseOver;
    private bool mouseDown;

    public DangerActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        ForeColor = Color.White;
        BackColor = Color.FromArgb(198, 67, 67);
        Cursor = Cursors.Default;
        Resize += (_, _) => UpdateRoundedRegion();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        UpdateRoundedRegion();
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        mouseOver = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        mouseOver = false;
        mouseDown = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        mouseDown = true;
        Invalidate();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        mouseDown = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = RoundedRectangle(bounds, 5);
        var fill = !Enabled
            ? Color.FromArgb(225, 196, 196)
            : mouseDown
                ? Color.FromArgb(174, 50, 50)
                : mouseOver
                    ? Color.FromArgb(210, 78, 78)
                    : Color.FromArgb(198, 67, 67);
        using var brush = new SolidBrush(fill);
        using var border = new Pen(Enabled ? Color.FromArgb(164, 45, 45) : Color.FromArgb(205, 180, 180));
        eventArgs.Graphics.FillPath(brush, path);
        eventArgs.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            ClientRectangle,
            Enabled ? Color.White : Color.FromArgb(245, 238, 238),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused) UiControls.HideFocusCue(this);
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 5);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
}

internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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

    public static Button StandardButton(string text)
    {
        if (text == "帳戶管理") text = "帳號管理";
        var button = IsDangerText(text) ? new DangerActionButton() : new NoFocusCueButton();
        button.Text = text;
        button.Width = StandardButtonWidth;
        button.Height = StandardButtonHeight;
        button.Margin = new Padding(6, 2, 6, 2);
        button.AutoSize = false;
        if (button is not DangerActionButton) button.UseVisualStyleBackColor = true;
        return button;
    }

    public static Button DangerButton(string text)
    {
        var button = new DangerActionButton
        {
            Text = text,
            Width = StandardButtonWidth,
            Height = StandardButtonHeight,
            Margin = new Padding(6, 2, 6, 2),
            AutoSize = false,
        };
        return button;
    }

    private static bool IsDangerText(string text) => text is "作廢" or "確認作廢" or "確認送出作廢";

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
